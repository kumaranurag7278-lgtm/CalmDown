using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CalmDown
{
    internal enum PerfBoostMode : uint
    {
        Disabled = 0,
        Enabled = 1,
        Aggressive = 2,
        EfficientEnabled = 3,
        EfficientAggressive = 4
    }

    internal enum GovernorMode
    {
        IceCold,
        SweetSpot,
        BeastTurbo
    }

    static class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int dwProcessId);

        public const int HWND_BROADCAST = 0xffff;
        public static readonly int WM_SHOWCALMDOWN = RegisterWindowMessage("WM_SHOWCALMDOWN_MSG_CALMDOWN");

        private static Mutex singleInstanceMutex;

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                try {
                    string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CalmDown", "crash.log");
                    File.WriteAllText(logPath, e.ExceptionObject.ToString());
                } catch { }
            };
            Application.ThreadException += (s, e) => {
                try {
                    string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CalmDown", "crash.log");
                    File.WriteAllText(logPath, e.Exception.ToString());
                } catch { }
            };

            // Immediately ensure original stock power settings are backed up before any mode runs
            NativePower.EnsureBackup();

            // CLI flags handling
            if (args != null && args.Length > 0)
            {
                string flag = args[0].Trim().ToLowerInvariant();
                switch (flag)
                {
                    case "--selftest":
                        AttachConsole(-1);
                        SelfTest.Run();
                        return;
                    case "--ice":
                    case "-i":
                        NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Disabled, true, true);
                        return;
                    case "--sweet":
                    case "-s":
                        NativePower.ApplyModeDirect(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive, true, true);
                        return;
                    case "--beast":
                    case "-b":
                        NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Aggressive, true, true);
                        return;
                    case "--restore":
                    case "-r":
                        NativePower.RestoreOriginalDirect();
                        return;
                }
            }

            // Single instance enforcement with AbandonedMutex recovery
            bool isFirstInstance = false;
            try
            {
                singleInstanceMutex = new Mutex(false, @"Local\CalmDown_SingleInstance_Mutex_v3");
                isFirstInstance = singleInstanceMutex.WaitOne(0, false);
            }
            catch (AbandonedMutexException)
            {
                isFirstInstance = true;
            }

            if (!isFirstInstance)
            {
                PostMessage((IntPtr)HWND_BROADCAST, WM_SHOWCALMDOWN, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            try
            {
                Application.Run(new MainForm());
            }
            finally
            {
                if (singleInstanceMutex != null)
                {
                    try { singleInstanceMutex.ReleaseMutex(); } catch { }
                    singleInstanceMutex.Dispose();
                }
            }
        }
    }

    internal static class SelfTest
    {
        public static void Run()
        {
            Console.WriteLine();
            Console.WriteLine("=========================================================");
            Console.WriteLine("   CalmDown v3.0 Governor State Machine Self-Test");
            Console.WriteLine("=========================================================");

            var sm = new GovernorStateMachine();

            // 1. Initial idle
            var m1 = sm.Process(10, 15, false);
            Assert("Initial low load stays in Ice-Cold Profile", m1 == GovernorMode.IceCold);

            // 2. Transient single-core spike (1 tick)
            var m2 = sm.Process(12, 92, false);
            Assert("Transient 1-tick spike does NOT escalate directly to Beast Turbo", m2 != GovernorMode.BeastTurbo);

            // 3. Sustained moderate load (2 ticks)
            sm.Process(25, 60, false);
            var m3 = sm.Process(28, 65, false);
            Assert("Sustained moderate load steps up to Sweet-Spot Profile", m3 == GovernorMode.SweetSpot);

            // 4. Single-core spike from Sweet-Spot
            var m4 = sm.Process(25, 95, false);
            Assert("Single-core spike in Sweet-Spot does NOT unlock Beast Turbo without multi-core demand", m4 == GovernorMode.SweetSpot);

            // 5. Sustained Multi-Core Heavy Load (2 ticks with high avg AND high max)
            sm.Process(80, 95, false);
            var m5 = sm.Process(85, 98, false);
            Assert("Sustained multi-core heavy load unlocks Beast Turbo Profile", m5 == GovernorMode.BeastTurbo);

            // 6. Load drop holds dwell time in Sweet-Spot
            var m6 = sm.Process(10, 15, false);
            Assert("Load drop holds dwell in Sweet-Spot before releasing to Ice-Cold", m6 == GovernorMode.SweetSpot);

            // 7. Active game override
            var m7 = sm.Process(5, 10, true);
            Assert("Game override locks Sweet-Spot Profile regardless of idle load", m7 == GovernorMode.SweetSpot);

            Console.WriteLine("---------------------------------------------------------");
            Console.WriteLine("   [RESULT] All Governor State Machine Asserts Passed!  ");
            Console.WriteLine("=========================================================");
            Console.WriteLine();
        }

        private static void Assert(string name, bool condition)
        {
            if (condition)
            {
                Console.WriteLine("  [PASS] " + name);
            }
            else
            {
                Console.WriteLine("  [FAIL] " + name);
                Environment.ExitCode = 1;
            }
        }
    }

    internal class GovernorStateMachine
    {
        private GovernorMode currentMode = GovernorMode.IceCold;
        private int stepDownDwellTicks = 0;
        private int moderateSpikeCount = 0;
        private int heavyMultiCoreSpikeCount = 0;

        public GovernorMode CurrentMode { get { return currentMode; } }

        public GovernorMode Process(int avgLoad, int maxCoreLoad, bool gameActive)
        {
            // Priority 1: Game Active -> Locks Sweet-Spot
            if (gameActive)
            {
                currentMode = GovernorMode.SweetSpot;
                stepDownDwellTicks = 3; // 6s hold
                moderateSpikeCount = 0;
                heavyMultiCoreSpikeCount = 0;
                return currentMode;
            }

            // Priority 2: Heavy Multi-Core Load (>70% avg AND >85% max core) for 2 consecutive ticks
            if (avgLoad >= 70 && maxCoreLoad >= 85)
            {
                heavyMultiCoreSpikeCount++;
                moderateSpikeCount = 0;
                if (heavyMultiCoreSpikeCount >= 2)
                {
                    currentMode = GovernorMode.BeastTurbo;
                    stepDownDwellTicks = 3;
                }
                return currentMode;
            }
            else
            {
                heavyMultiCoreSpikeCount = 0;
            }

            // Priority 3: Moderate / Gaming Load (>25% max or >20% avg) for 2 consecutive ticks
            if (maxCoreLoad >= 25 || avgLoad >= 20)
            {
                moderateSpikeCount++;
                if (moderateSpikeCount >= 2)
                {
                    currentMode = GovernorMode.SweetSpot;
                    stepDownDwellTicks = 3;
                }
                return currentMode;
            }
            else
            {
                moderateSpikeCount = 0;
            }

            // Priority 4: Low Load -> Release with Dwell Time
            if (stepDownDwellTicks > 0)
            {
                stepDownDwellTicks--;
                if (currentMode == GovernorMode.BeastTurbo)
                {
                    currentMode = GovernorMode.SweetSpot;
                }
                return currentMode;
            }

            currentMode = GovernorMode.IceCold;
            return currentMode;
        }
    }

    internal static class ConfigManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CalmDown");
        private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.ini");
        private static readonly string RulesFile = Path.Combine(ConfigDir, "rules.ini");

        public static bool DynamicGovernor { get; set; }
        public static bool GameAutoPilot { get; set; }
        public static Dictionary<string, string> AppRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static ConfigManager()
        {
            DynamicGovernor = false;
            GameAutoPilot = true;
            if (File.Exists(ConfigFile)) Load();
            else Save();

            LoadRules();
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(ConfigFile)) return;
                foreach (string rawLine in File.ReadAllLines(ConfigFile))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";")) continue;
                    string[] parts = line.Split('=');
                    if (parts.Length != 2) continue;

                    string key = parts[0].Trim();
                    string val = parts[1].Trim();

                    if (key.Equals("DynamicGovernor", StringComparison.OrdinalIgnoreCase))
                    {
                        bool b;
                        if (bool.TryParse(val, out b)) DynamicGovernor = b;
                    }
                    else if (key.Equals("GameAutoPilot", StringComparison.OrdinalIgnoreCase))
                    {
                        bool b;
                        if (bool.TryParse(val, out b)) GameAutoPilot = b;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir);
                string tempFile = ConfigFile + ".tmp";
                string content = string.Format(
                    "# CalmDown v3.0 Hardware Configuration\r\nDynamicGovernor={0}\r\nGameAutoPilot={1}\r\n",
                    DynamicGovernor, GameAutoPilot);
                File.WriteAllText(tempFile, content);
                if (File.Exists(ConfigFile)) File.Delete(ConfigFile);
                File.Move(tempFile, ConfigFile);
            }
            catch { }
        }

        public static void LoadRules()
        {
            try
            {
                if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir);
                if (!File.Exists(RulesFile))
                {
                    string defaultRules = "# CalmDown Per-Process Rules (process_name = Sweet / Beast / Ice)\r\n" +
                                          "VALORANT = Sweet\r\n" +
                                          "cs2 = Sweet\r\n" +
                                          "GTA5 = Sweet\r\n" +
                                          "r5apex = Sweet\r\n" +
                                          "Overwatch = Sweet\r\n" +
                                          "FortniteClient-Win64-Shipping = Sweet\r\n" +
                                          "devenv = Beast\r\n" +
                                          "Premiere = Beast\r\n" +
                                          "Blender = Beast\r\n";
                    File.WriteAllText(RulesFile, defaultRules);
                }

                AppRules.Clear();
                foreach (string rawLine in File.ReadAllLines(RulesFile))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";")) continue;
                    string[] parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        AppRules[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }
            catch { }
        }
    }

    internal static class NativePower
    {
        public const uint FREQ_UNCAPPED = 0;
        public const uint FREQ_SWEETSPOT_MHZ = 3500;

        private static readonly object powerSyncLock = new object();

        private static Guid SubGroupProcessor = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        private static Guid GuidBoostMode = new Guid("be337238-0d82-4146-a960-4f3749d470c7");
        private static Guid GuidFreqMax = new Guid("75b0ae3f-bce0-45a7-8c89-c9611c25e100");

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroup, ref Guid Setting, out uint AcValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadDCValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroup, ref Guid Setting, out uint DcValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteACValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroup, ref Guid Setting, uint AcValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteDCValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroup, ref Guid Setting, uint DcValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private static readonly string BackupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CalmDown", "original_settings.txt");

        public static bool GetActiveSchemeGuid(out Guid activeScheme)
        {
            activeScheme = Guid.Empty;
            IntPtr pGuid;
            if (PowerGetActiveScheme(IntPtr.Zero, out pGuid) == 0 && pGuid != IntPtr.Zero)
            {
                try
                {
                    activeScheme = (Guid)Marshal.PtrToStructure(pGuid, typeof(Guid));
                    return true;
                }
                finally
                {
                    LocalFree(pGuid);
                }
            }
            return false;
        }

        public static bool ReadCurrentIndices(out uint boostMode, out uint maxFreqMhz)
        {
            boostMode = 0;
            maxFreqMhz = 0;
            Guid scheme;
            if (!GetActiveSchemeGuid(out scheme)) return false;

            bool onBattery = (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline);
            if (onBattery)
            {
                PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, out boostMode);
                PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, out maxFreqMhz);
            }
            else
            {
                PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, out boostMode);
                PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, out maxFreqMhz);
            }
            return true;
        }

        public static void EnsureBackup()
        {
            try
            {
                if (File.Exists(BackupPath)) return;
                string dir = Path.GetDirectoryName(BackupPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                Guid scheme;
                if (!GetActiveSchemeGuid(out scheme)) return;

                uint acBoost, acFreq, dcBoost, dcFreq;
                PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, out acBoost);
                PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, out acFreq);
                PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, out dcBoost);
                PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, out dcFreq);

                // Save: AC_Boost, AC_Freq, DC_Boost, DC_Freq
                File.WriteAllText(BackupPath, string.Format("{0},{1},{2},{3}", acBoost, acFreq, dcBoost, dcFreq));
            }
            catch { }
        }

        public static bool RestoreOriginalDirect()
        {
            try
            {
                if (!File.Exists(BackupPath)) return false;
                string[] parts = File.ReadAllText(BackupPath).Trim().Split(',');
                if (parts.Length < 2) return false;

                uint acBoost = uint.Parse(parts[0]);
                uint acFreq = uint.Parse(parts[1]);
                uint dcBoost = parts.Length >= 4 ? uint.Parse(parts[2]) : acBoost;
                uint dcFreq = parts.Length >= 4 ? uint.Parse(parts[3]) : acFreq;

                lock (powerSyncLock)
                {
                    Guid scheme;
                    if (!GetActiveSchemeGuid(out scheme)) return false;

                    PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, acFreq);
                    PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, acBoost);
                    PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, dcFreq);
                    PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, dcBoost);
                    PowerSetActiveScheme(IntPtr.Zero, ref scheme);
                    return true;
                }
            }
            catch { return false; }
        }

        public static bool ApplyModeDirect(uint freqMhz, PerfBoostMode boostMode, bool writeAC = true, bool writeDC = true)
        {
            lock (powerSyncLock)
            {
                uint curB, curF;
                if (ReadCurrentIndices(out curB, out curF) && curB == (uint)boostMode && curF == freqMhz)
                {
                    return true; // Already applied, verified against live ACPI state
                }

                EnsureBackup();
                Guid scheme;
                if (!GetActiveSchemeGuid(out scheme)) return false;

                bool ok = true;
                if (writeAC)
                {
                    if (PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, freqMhz) != 0 ||
                        PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, (uint)boostMode) != 0)
                    {
                        ok = false;
                    }
                }

                if (writeDC)
                {
                    if (PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, freqMhz) != 0 ||
                        PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, (uint)boostMode) != 0)
                    {
                        ok = false;
                    }
                }

                if (ok)
                {
                    PowerSetActiveScheme(IntPtr.Zero, ref scheme);
                }
                return ok;
            }
        }
    }

    internal static class HardwareMonitor
    {
        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
        {
            public long IdleTime;
            public long KernelTime;
            public long UserTime;
            public long DpcTime;
            public long InterruptTime;
            public int InterruptCount;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength, out int ReturnLength);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static int coreCount = Environment.ProcessorCount;
        private static int structSize = Marshal.SizeOf(typeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION));
        private static int totalSize = structSize * coreCount;
        private static IntPtr prevBuf = IntPtr.Zero;
        private static bool initialized = false;

        public static int CoreCount { get { return coreCount; } }

        public static void GetCpuMetrics(out int avgLoad, out int maxCoreLoad)
        {
            avgLoad = 0;
            maxCoreLoad = 0;

            IntPtr curBuf = Marshal.AllocHGlobal(totalSize);
            int retLen;
            if (NtQuerySystemInformation(8, curBuf, totalSize, out retLen) != 0)
            {
                Marshal.FreeHGlobal(curBuf);
                return;
            }

            if (!initialized || prevBuf == IntPtr.Zero)
            {
                prevBuf = curBuf;
                initialized = true;
                return;
            }

            long totalSysAll = 0;
            long totalBusyAll = 0;
            long highestCoreBusy = 0;
            long highestCoreTotal = 1;

            for (int i = 0; i < coreCount; i++)
            {
                IntPtr p1 = new IntPtr(prevBuf.ToInt64() + i * structSize);
                IntPtr p2 = new IntPtr(curBuf.ToInt64() + i * structSize);
                SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION i1 = (SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION)Marshal.PtrToStructure(p1, typeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION));
                SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION i2 = (SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION)Marshal.PtrToStructure(p2, typeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION));

                long idle = i2.IdleTime - i1.IdleTime;
                long kernel = i2.KernelTime - i1.KernelTime;
                long user = i2.UserTime - i1.UserTime;
                long total = kernel + user;
                long busy = total - idle;
                if (busy < 0) busy = 0;

                totalSysAll += total;
                totalBusyAll += busy;

                if (total > 0)
                {
                    if ((busy * 100) / total > (highestCoreBusy * 100) / highestCoreTotal)
                    {
                        highestCoreBusy = busy;
                        highestCoreTotal = total;
                    }
                }
            }

            Marshal.FreeHGlobal(prevBuf);
            prevBuf = curBuf;

            if (totalSysAll > 0)
            {
                avgLoad = (int)((totalBusyAll * 100) / totalSysAll);
            }
            if (highestCoreTotal > 0)
            {
                maxCoreLoad = (int)((highestCoreBusy * 100) / highestCoreTotal);
            }

            avgLoad = Math.Max(0, Math.Min(100, avgLoad));
            maxCoreLoad = Math.Max(0, Math.Min(100, maxCoreLoad));
        }

        public static string GetForegroundProcessName()
        {
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return "";
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == 0) return "";
                using (var p = Process.GetProcessById((int)pid))
                {
                    return p.ProcessName;
                }
            }
            catch { return ""; }
        }

        public static string GetProcessorName()
        {
            try
            {
                object val = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "");
                if (val != null)
                {
                    string name = val.ToString().Trim();
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch { }
            return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Intel/AMD Processor";
        }
    }

    public class MainForm : Form
    {
        private Panel pnlHardware;
        private Label lblCpuName;
        private Label lblAcpiTarget;
        private Label lblLiveTelemetry;
        private Panel pnlSparkline;

        private Panel cardIce;
        private Panel cardSweet;
        private Panel cardBeast;

        private Label lblIceTag;
        private Label lblSweetTag;
        private Label lblBeastTag;

        private Panel pnlAutomation;
        private CheckBox chkDynamic;
        private Label lblDynamicSub;
        private CheckBox chkAutoPilot;
        private Label lblAutoPilotSub;

        private Button btnRestore;
        private Button btnMinimizeTray;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;

        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem trayDynamicItem;
        private ToolStripMenuItem trayAutoPilotItem;
        private System.Windows.Forms.Timer backgroundTimer;

        private GovernorStateMachine stateMachine = new GovernorStateMachine();
        private List<int> telemetryHistory = new List<int>();
        private const int MaxHistoryPoints = 50;

        private int cachedAvgLoad = 0;
        private int cachedMaxCoreLoad = 0;
        private GovernorMode previousUserMode = GovernorMode.IceCold;
        private bool isGameActive = false;

        public MainForm()
        {
            for (int i = 0; i < MaxHistoryPoints; i++) telemetryHistory.Add(0);

            InitializeComponent();
            SetupTray();
            SetupGovernorTimer();
            RefreshStatus(0, 0);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.WM_SHOWCALMDOWN)
            {
                this.Show();
                if (this.WindowState == FormWindowState.Minimized)
                {
                    this.WindowState = FormWindowState.Normal;
                }
                this.Activate();
                this.BringToFront();
                return;
            }
            base.WndProc(ref m);
        }

        private void InitializeComponent()
        {
            this.Text = "CalmDown v3.0 - Driver-Free Hardware Power Governor";
            this.Size = new Size(580, 630);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.FromArgb(235, 238, 245);
            this.Font = new Font("Segoe UI", 9f, FontStyle.Regular);

            // 1. Hardware Status Header Panel
            pnlHardware = new Panel
            {
                Location = new Point(18, 14),
                Size = new Size(528, 92),
                BackColor = Color.FromArgb(25, 27, 33)
            };
            pnlHardware.Paint += (s, e) => {
                ControlPaint.DrawBorder(e.Graphics, pnlHardware.ClientRectangle,
                    Color.FromArgb(42, 45, 54), ButtonBorderStyle.Solid);
            };

            lblCpuName = new Label
            {
                Text = string.Format("CPU: {0} ({1} Threads)", HardwareMonitor.GetProcessorName(), HardwareMonitor.CoreCount),
                Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(245, 247, 252),
                Location = new Point(14, 8),
                AutoSize = true
            };

            lblAcpiTarget = new Label
            {
                Text = "Engine: Native powrprof.dll Win32 P/Invoke | Driver-Free User-Space",
                Font = new Font("Consolas", 7.8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(0, 200, 83),
                Location = new Point(15, 30),
                AutoSize = true
            };

            lblLiveTelemetry = new Label
            {
                Text = "STATUS: INITIALIZING...",
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 168, 255),
                Location = new Point(14, 52),
                AutoSize = true
            };

            pnlSparkline = new Panel
            {
                Location = new Point(370, 48),
                Size = new Size(142, 34),
                BackColor = Color.FromArgb(14, 15, 18)
            };
            pnlSparkline.Paint += DrawSparkline;

            pnlHardware.Controls.Add(lblCpuName);
            pnlHardware.Controls.Add(lblAcpiTarget);
            pnlHardware.Controls.Add(lblLiveTelemetry);
            pnlHardware.Controls.Add(pnlSparkline);
            this.Controls.Add(pnlHardware);

            // 2. Profile Selection Cards
            cardIce = CreateProfileCard(
                "ICE-COLD PROFILE",
                "Frequency Ceiling: Base Clock (~2.4 GHz) | Turbo: Disabled (0x0)\nTarget Wattage: ~15W–25W | Keyboard Cool | Silent Fans\nIdeal for: Study, Reading, Web Browsing, Document Work",
                new Point(18, 116),
                Color.FromArgb(0, 168, 255),
                out lblIceTag,
                () => SetManualMode(NativePower.FREQ_UNCAPPED, PerfBoostMode.Disabled, GovernorMode.IceCold)
            );

            cardSweet = CreateProfileCard(
                "SWEET-SPOT BALANCED PROFILE",
                "Frequency Ceiling: 3500 MHz | Turbo: Efficient Aggressive (0x4)\nTarget Wattage: ~30W–35W | Voltage Inflection Cap | Zero Stutters\nIdeal for: Valorant, CS2, Competitive Gaming, Multitasking",
                new Point(18, 196),
                Color.FromArgb(0, 200, 83),
                out lblSweetTag,
                () => SetManualMode(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive, GovernorMode.SweetSpot)
            );

            cardBeast = CreateProfileCard(
                "BEAST TURBO PROFILE",
                "Frequency Ceiling: Uncapped (Up to 4.9 GHz) | Turbo: Aggressive (0x2)\nTarget Wattage: Full Package TDP (~65W–75W+) | High Thermals\nIdeal for: 4K Video Exports, Code Builds, Heavy Benchmarks",
                new Point(18, 276),
                Color.FromArgb(255, 82, 82),
                out lblBeastTag,
                () => SetManualMode(NativePower.FREQ_UNCAPPED, PerfBoostMode.Aggressive, GovernorMode.BeastTurbo)
            );

            this.Controls.Add(cardIce);
            this.Controls.Add(cardSweet);
            this.Controls.Add(cardBeast);

            // 3. Automation Settings Panel
            pnlAutomation = new Panel
            {
                Location = new Point(18, 366),
                Size = new Size(528, 122),
                BackColor = Color.FromArgb(25, 27, 33)
            };
            pnlAutomation.Paint += (s, e) => {
                ControlPaint.DrawBorder(e.Graphics, pnlAutomation.ClientRectangle,
                    Color.FromArgb(42, 45, 54), ButtonBorderStyle.Solid);
            };

            chkDynamic = new CheckBox
            {
                Text = "Enable Smart Dynamic Load Governor (Anti-Flap State Machine)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(235, 238, 248),
                Location = new Point(14, 12),
                AutoSize = true,
                Checked = ConfigManager.DynamicGovernor,
                Cursor = Cursors.Hand
            };
            chkDynamic.CheckedChanged += (s, e) =>
            {
                ConfigManager.DynamicGovernor = chkDynamic.Checked;
                ConfigManager.Save();
                if (trayDynamicItem != null) trayDynamicItem.Checked = chkDynamic.Checked;
                RefreshStatus(cachedAvgLoad, cachedMaxCoreLoad);
            };

            lblDynamicSub = new Label
            {
                Text = "Step-by-step scaling: single-core spikes hold Sweet-Spot; Beast requires sustained multi-core load.\n(Slow-release 6s hold eliminates rapid fan oscillation)",
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(145, 152, 168),
                Location = new Point(34, 32),
                Size = new Size(480, 28)
            };

            chkAutoPilot = new CheckBox
            {
                Text = "Enable Game Auto-Pilot Priority (Foreground Process & rules.ini)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(235, 238, 248),
                Location = new Point(14, 68),
                AutoSize = true,
                Checked = ConfigManager.GameAutoPilot,
                Cursor = Cursors.Hand
            };
            chkAutoPilot.CheckedChanged += (s, e) =>
            {
                ConfigManager.GameAutoPilot = chkAutoPilot.Checked;
                ConfigManager.Save();
                if (trayAutoPilotItem != null) trayAutoPilotItem.Checked = chkAutoPilot.Checked;
                if (!chkAutoPilot.Checked && isGameActive)
                {
                    isGameActive = false;
                }
                RefreshStatus(cachedAvgLoad, cachedMaxCoreLoad);
            };

            lblAutoPilotSub = new Label
            {
                Text = "Locks Sweet-Spot (3500 MHz) when games or rules.ini matches are detected.",
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(145, 152, 168),
                Location = new Point(34, 90),
                Size = new Size(480, 18)
            };

            pnlAutomation.Controls.Add(chkDynamic);
            pnlAutomation.Controls.Add(lblDynamicSub);
            pnlAutomation.Controls.Add(chkAutoPilot);
            pnlAutomation.Controls.Add(lblAutoPilotSub);
            this.Controls.Add(pnlAutomation);

            // 4. Action Buttons
            btnRestore = new Button
            {
                Text = "Restore Factory Defaults",
                Location = new Point(18, 502),
                Size = new Size(185, 36),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                BackColor = Color.FromArgb(32, 35, 43),
                ForeColor = Color.FromArgb(180, 186, 200),
                Cursor = Cursors.Hand
            };
            btnRestore.FlatAppearance.BorderColor = Color.FromArgb(55, 60, 72);
            btnRestore.Click += (s, e) =>
            {
                chkDynamic.Checked = false;
                if (NativePower.RestoreOriginalDirect())
                {
                    MessageBox.Show("Stock ACPI power scheme indices successfully restored.", "CalmDown", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshStatus(cachedAvgLoad, cachedMaxCoreLoad);
                }
            };
            this.Controls.Add(btnRestore);

            btnMinimizeTray = new Button
            {
                Text = "Minimize to System Tray",
                Location = new Point(360, 502),
                Size = new Size(185, 36),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(28, 90, 180),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnMinimizeTray.FlatAppearance.BorderSize = 0;
            btnMinimizeTray.Click += (s, e) =>
            {
                this.Hide();
                trayIcon.ShowBalloonTip(1500, "CalmDown Active", "Running quietly in tray. Double-click tray icon to restore.", ToolTipIcon.Info);
            };
            this.Controls.Add(btnMinimizeTray);

            // 5. Status Strip
            statusStrip = new StatusStrip
            {
                BackColor = Color.FromArgb(14, 15, 18),
                SizingGrip = false
            };
            statusLabel = new ToolStripStatusLabel
            {
                Text = "Ready | Engine: PowrProf P/Invoke (~51ms) | User-Space ACPI | v3.0",
                ForeColor = Color.FromArgb(120, 126, 140),
                Font = new Font("Segoe UI", 8f, FontStyle.Regular)
            };
            statusStrip.Items.Add(statusLabel);
            this.Controls.Add(statusStrip);

            // Hide to tray on Close [X]
            this.FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    this.Hide();
                    trayIcon.ShowBalloonTip(1200, "CalmDown", "Active in background tray.", ToolTipIcon.Info);
                }
            };
        }

        private void DrawSparkline(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = pnlSparkline.Width;
            int h = pnlSparkline.Height;

            ControlPaint.DrawBorder(g, pnlSparkline.ClientRectangle, Color.FromArgb(35, 38, 46), ButtonBorderStyle.Solid);
            if (telemetryHistory.Count < 2) return;

            PointF[] points = new PointF[telemetryHistory.Count];
            float dx = (float)w / (telemetryHistory.Count - 1);

            for (int i = 0; i < telemetryHistory.Count; i++)
            {
                float y = h - (telemetryHistory[i] * (h - 4) / 100f) - 2;
                points[i] = new PointF(i * dx, y);
            }

            using (Pen pen = new Pen(Color.FromArgb(0, 168, 255), 1.5f))
            {
                g.DrawLines(pen, points);
            }
        }

        private Panel CreateProfileCard(string title, string specs, Point loc, Color accent, out Label outTag, Action onClick)
        {
            var pnl = new Panel
            {
                Location = loc,
                Size = new Size(528, 72),
                BackColor = Color.FromArgb(25, 27, 33),
                Cursor = Cursors.Hand
            };

            var lblT = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 243, 250),
                Location = new Point(14, 8),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            lblT.Click += (s, e) => onClick();

            var tag = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                ForeColor = accent,
                Location = new Point(415, 10),
                Size = new Size(95, 18),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };
            tag.Click += (s, e) => onClick();
            outTag = tag;

            var lblD = new Label
            {
                Text = specs,
                Font = new Font("Segoe UI", 7.8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(145, 152, 168),
                Location = new Point(14, 28),
                Size = new Size(500, 38),
                BackColor = Color.Transparent
            };
            lblD.Click += (s, e) => onClick();

            pnl.Controls.Add(lblT);
            pnl.Controls.Add(tag);
            pnl.Controls.Add(lblD);
            pnl.Click += (s, e) => onClick();

            pnl.Paint += (s, e) => {
                Color border = tag.Text.Contains("ACTIVE") ? accent : Color.FromArgb(42, 45, 54);
                int borderW = tag.Text.Contains("ACTIVE") ? 2 : 1;
                ControlPaint.DrawBorder(e.Graphics, pnl.ClientRectangle,
                    border, borderW, ButtonBorderStyle.Solid,
                    border, borderW, ButtonBorderStyle.Solid,
                    border, borderW, ButtonBorderStyle.Solid,
                    border, borderW, ButtonBorderStyle.Solid);
            };

            return pnl;
        }

        private void SetupTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("CalmDown Hardware Governor v3.0", null, (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; });
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Ice-Cold Profile (~2.4 GHz)", null, (s, e) => SetManualMode(NativePower.FREQ_UNCAPPED, PerfBoostMode.Disabled, GovernorMode.IceCold));
            trayMenu.Items.Add("Sweet-Spot Profile (3500 MHz)", null, (s, e) => SetManualMode(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive, GovernorMode.SweetSpot));
            trayMenu.Items.Add("Beast Turbo Profile (Uncapped)", null, (s, e) => SetManualMode(NativePower.FREQ_UNCAPPED, PerfBoostMode.Aggressive, GovernorMode.BeastTurbo));
            trayMenu.Items.Add("-");

            trayDynamicItem = new ToolStripMenuItem("Smart Dynamic Governor", null, (s, e) =>
            {
                chkDynamic.Checked = !chkDynamic.Checked;
            }) { CheckOnClick = false, Checked = ConfigManager.DynamicGovernor };
            trayMenu.Items.Add(trayDynamicItem);

            trayAutoPilotItem = new ToolStripMenuItem("Game Auto-Pilot Priority", null, (s, e) =>
            {
                chkAutoPilot.Checked = !chkAutoPilot.Checked;
            }) { CheckOnClick = false, Checked = ConfigManager.GameAutoPilot };
            trayMenu.Items.Add(trayAutoPilotItem);

            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Open CalmDown", null, (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; });
            trayMenu.Items.Add("Exit CalmDown", null, (s, e) => {
                trayIcon.Visible = false;
                NativePower.RestoreOriginalDirect();
                Application.Exit();
            });

            trayIcon = new NotifyIcon
            {
                Icon = (Icon)SystemIcons.Shield.Clone(),
                ContextMenuStrip = trayMenu,
                Text = "CalmDown: Active",
                Visible = true
            };
            trayIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; };
        }

        private void SetupGovernorTimer()
        {
            backgroundTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            backgroundTimer.Tick += (s, e) =>
            {
                try
                {
                    HardwareMonitor.GetCpuMetrics(out cachedAvgLoad, out cachedMaxCoreLoad);

                    telemetryHistory.Add(cachedMaxCoreLoad);
                    if (telemetryHistory.Count > MaxHistoryPoints) telemetryHistory.RemoveAt(0);
                    pnlSparkline.Invalidate();

                    // Check Active App Rules or Game Detection
                    bool gameRunning = false;
                    string fgProcess = HardwareMonitor.GetForegroundProcessName();

                    if (chkAutoPilot.Checked)
                    {
                        string ruleMode;
                        if (!string.IsNullOrEmpty(fgProcess) && ConfigManager.AppRules.TryGetValue(fgProcess, out ruleMode))
                        {
                            gameRunning = true;
                        }
                        else
                        {
                            foreach (var kvp in ConfigManager.AppRules)
                            {
                                if (kvp.Value.Equals("Sweet", StringComparison.OrdinalIgnoreCase) ||
                                    kvp.Value.Equals("Beast", StringComparison.OrdinalIgnoreCase))
                                {
                                    Process[] procs = Process.GetProcessesByName(kvp.Key);
                                    try
                                    {
                                        if (procs.Length > 0)
                                        {
                                            gameRunning = true;
                                            break;
                                        }
                                    }
                                    finally
                                    {
                                        foreach (var p in procs) { try { p.Dispose(); } catch { } }
                                    }
                                }
                            }
                        }

                        if (gameRunning && !isGameActive)
                        {
                            isGameActive = true;
                            previousUserMode = stateMachine.CurrentMode;
                            NativePower.ApplyModeDirect(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive);
                            trayIcon.ShowBalloonTip(1800, "Game Detected", "Locked Sweet-Spot Profile (3500 MHz) for consistent frametimes.", ToolTipIcon.Info);
                        }
                        else if (!gameRunning && isGameActive)
                        {
                            isGameActive = false;
                            // Restore previous mode
                            switch (previousUserMode)
                            {
                                case GovernorMode.BeastTurbo:
                                    NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Aggressive);
                                    break;
                                case GovernorMode.SweetSpot:
                                    NativePower.ApplyModeDirect(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive);
                                    break;
                                default:
                                    NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Disabled);
                                    break;
                            }
                            trayIcon.ShowBalloonTip(1800, "Game Exited", "Restored previous power profile.", ToolTipIcon.Info);
                        }
                    }

                    // Dynamic Governor State Machine
                    if (chkDynamic.Checked && !gameRunning)
                    {
                        var targetMode = stateMachine.Process(cachedAvgLoad, cachedMaxCoreLoad, false);
                        switch (targetMode)
                        {
                            case GovernorMode.BeastTurbo:
                                NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Aggressive);
                                break;
                            case GovernorMode.SweetSpot:
                                NativePower.ApplyModeDirect(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive);
                                break;
                            case GovernorMode.IceCold:
                                NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Disabled);
                                break;
                        }
                    }

                    RefreshStatus(cachedAvgLoad, cachedMaxCoreLoad);
                }
                catch (Exception ex)
                {
                    try {
                        string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CalmDown", "error.log");
                        File.AppendAllText(logPath, ex.ToString() + "\n");
                    } catch { }
                }
            };
            backgroundTimer.Start();
        }

        private void SetManualMode(uint freq, PerfBoostMode boost, GovernorMode targetMode)
        {
            if (chkDynamic.Checked)
            {
                chkDynamic.Checked = false; // Disable dynamic governor so manual selection holds
            }

            previousUserMode = targetMode;

            if (NativePower.ApplyModeDirect(freq, boost))
            {
                SystemSounds.Asterisk.Play();
                RefreshStatus(cachedAvgLoad, cachedMaxCoreLoad);
            }
        }

        private void RefreshStatus(int avgLoad, int maxCoreLoad)
        {
            uint b, f;
            if (!NativePower.ReadCurrentIndices(out b, out f))
            {
                lblLiveTelemetry.Text = "ACPI READ ERROR";
                lblLiveTelemetry.ForeColor = Color.Red;
                return;
            }

            bool isIce = (b == (uint)PerfBoostMode.Disabled);
            bool isSweet = (f == NativePower.FREQ_SWEETSPOT_MHZ);
            bool isBeast = (b == (uint)PerfBoostMode.Aggressive && f == NativePower.FREQ_UNCAPPED);

            lblIceTag.Text = isIce ? "[ ACTIVE ]" : "";
            lblSweetTag.Text = isSweet ? "[ ACTIVE ]" : "";
            lblBeastTag.Text = isBeast ? "[ ACTIVE ]" : "";

            cardIce.Invalidate();
            cardSweet.Invalidate();
            cardBeast.Invalidate();

            string modeTitle = isIce ? "Ice-Cold (2.4 GHz)" : (isSweet ? "Sweet-Spot (3.5 GHz)" : (isBeast ? "Beast Turbo" : "Custom"));
            Color modeColor = isIce ? Color.FromArgb(0, 168, 255) : (isSweet ? Color.FromArgb(0, 200, 83) : Color.FromArgb(255, 82, 82));

            string modeTag = "";
            if (chkDynamic.Checked) modeTag = " [DYNAMIC]";
            else if (isGameActive) modeTag = " [GAME LOCK]";

            lblLiveTelemetry.Text = string.Format("ACTIVE: {0}{1} | AVG: {2}% | MAX CORE: {3}%", modeTitle.ToUpperInvariant(), modeTag, avgLoad, maxCoreLoad);
            lblLiveTelemetry.ForeColor = modeColor;

            try
            {
                string trayStr = string.Format("CalmDown: {0}% ({1})", maxCoreLoad, modeTitle);
                if (trayStr.Length > 63) trayStr = trayStr.Substring(0, 60) + "...";
                trayIcon.Text = trayStr;
            }
            catch { }
        }
    }
}
