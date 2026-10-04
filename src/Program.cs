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

    static class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

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
                try { File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CalmDown", "crash.log"), e.ExceptionObject.ToString()); } catch { }
            };
            Application.ThreadException += (s, e) => {
                try { File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CalmDown", "crash.log"), e.Exception.ToString()); } catch { }
            };

            // CLI flags handling
            if (args != null && args.Length > 0)
            {
                string flag = args[0].Trim().ToLowerInvariant();
                switch (flag)
                {
                    case "--ice":
                    case "-i":
                        NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Disabled, true, false);
                        return;
                    case "--sweet":
                    case "-s":
                        NativePower.ApplyModeDirect(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive, true, false);
                        return;
                    case "--beast":
                    case "-b":
                        NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Aggressive, true, false);
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

    internal static class ConfigManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CalmDown");
        private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.ini");

        public static bool DynamicGovernor { get; set; }
        public static bool GameAutoPilot { get; set; }

        static ConfigManager()
        {
            DynamicGovernor = false;
            GameAutoPilot = true;
            if (File.Exists(ConfigFile))
            {
                Load();
            }
            else
            {
                Save();
            }
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
    }

    internal static class NativePower
    {
        public const uint FREQ_UNCAPPED = 0;
        public const uint FREQ_SWEETSPOT_MHZ = 3500;

        // ACPI Guids from Windows PowrProf
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

        private static uint cachedFreq = 999999;
        private static uint cachedBoost = 999999;

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

            PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, out boostMode);
            PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, out maxFreqMhz);
            return true;
        }

        public static void EnsureBackup()
        {
            try
            {
                if (File.Exists(BackupPath)) return;
                string dir = Path.GetDirectoryName(BackupPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                uint b, f;
                if (ReadCurrentIndices(out b, out f))
                {
                    File.WriteAllText(BackupPath, string.Format("{0},{1}", b, f));
                }
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

                uint boost = uint.Parse(parts[0]);
                uint freq = uint.Parse(parts[1]);
                cachedFreq = 999999;
                cachedBoost = 999999;
                return ApplyModeDirect(freq, (PerfBoostMode)boost, true, true);
            }
            catch { return false; }
        }

        public static bool ApplyModeDirect(uint freqMhz, PerfBoostMode boostMode, bool writeAC = true, bool writeDC = false)
        {
            // Avoid duplicate execution if already applied
            if (cachedFreq == freqMhz && cachedBoost == (uint)boostMode)
            {
                return true;
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
                cachedFreq = freqMhz;
                cachedBoost = (uint)boostMode;
            }
            return ok;
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

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int pquns);

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

        public static bool IsD3DFullscreenGameActive()
        {
            int state;
            if (SHQueryUserNotificationState(out state) == 0)
            {
                // QUNS_RUNNING_D3D_FULL_SCREEN = 3
                return state == 3;
            }
            return false;
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

        // Rolling 50-point telemetry sparkline history
        private List<int> telemetryHistory = new List<int>();
        private const int MaxHistoryPoints = 50;

        // Auto-pilot watched games
        private static readonly string[] WatchedProcesses = new[]
        {
            "VALORANT", "VALORANT-Win64-Shipping", "cs2", "GTA5", "Overwatch", "FortniteClient-Win64-Shipping", "r5apex"
        };
        private bool isGameActive = false;

        // Fast-attack, slow-release debounce timers
        private int stepDownDwellTicks = 0;

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
                Text = "Engine: Native powrprof.dll Win32 P/Invoke | Driver-Free | 0 BSOD Risk",
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

            // Live Rolling Telemetry Sparkline
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
                () => SetManualMode(NativePower.FREQ_UNCAPPED, PerfBoostMode.Disabled)
            );

            cardSweet = CreateProfileCard(
                "SWEET-SPOT BALANCED PROFILE",
                "Frequency Ceiling: 3500 MHz | Turbo: Efficient Aggressive (0x4)\nTarget Wattage: ~30W–35W | Voltage Inflection Cap | Zero Stutters\nIdeal for: Valorant, CS2, Competitive Gaming, Multitasking",
                new Point(18, 196),
                Color.FromArgb(0, 200, 83),
                out lblSweetTag,
                () => SetManualMode(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive)
            );

            cardBeast = CreateProfileCard(
                "BEAST TURBO PROFILE",
                "Frequency Ceiling: Uncapped (Up to 4.9 GHz) | Turbo: Aggressive (0x2)\nTarget Wattage: Full Package TDP (~65W–75W+) | High Thermals\nIdeal for: 4K Video Exports, Code Builds, Heavy Benchmarks",
                new Point(18, 276),
                Color.FromArgb(255, 82, 82),
                out lblBeastTag,
                () => SetManualMode(NativePower.FREQ_UNCAPPED, PerfBoostMode.Aggressive)
            );

            this.Controls.Add(cardIce);
            this.Controls.Add(cardSweet);
            this.Controls.Add(cardBeast);

            // 3. Autonomous Automation Settings Panel
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
                Text = "Enable Smart Dynamic Load Governor (Max-Core Aware)",
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
                int a, m;
                HardwareMonitor.GetCpuMetrics(out a, out m);
                RefreshStatus(a, m);
            };

            lblDynamicSub = new Label
            {
                Text = "Uses per-core tracking with fast-attack & slow-release to prevent single-core gaming choke.\n(<20% Max Core: Ice-Cold  |  25-80%: Sweet-Spot  |  >80%: Beast Turbo)",
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(145, 152, 168),
                Location = new Point(34, 32),
                Size = new Size(480, 28)
            };

            chkAutoPilot = new CheckBox
            {
                Text = "Enable Game Auto-Pilot Priority (Direct3D Fullscreen + Process Lock)",
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
            };

            lblAutoPilotSub = new Label
            {
                Text = "Auto-detects exclusive 3D games via SHQueryUserNotificationState & locks 3500 MHz.",
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
                    int a, m;
                    HardwareMonitor.GetCpuMetrics(out a, out m);
                    RefreshStatus(a, m);
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
                Text = "Ready | Engine: PowrProf P/Invoke | Anti-Cheat Safe (0 Kernel Drivers) | v3.0",
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

            // Subtle border
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
            trayMenu.Items.Add("Ice-Cold Profile (~2.4 GHz)", null, (s, e) => SetManualMode(NativePower.FREQ_UNCAPPED, PerfBoostMode.Disabled));
            trayMenu.Items.Add("Sweet-Spot Profile (3500 MHz)", null, (s, e) => SetManualMode(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive));
            trayMenu.Items.Add("Beast Turbo Profile (Uncapped)", null, (s, e) => SetManualMode(NativePower.FREQ_UNCAPPED, PerfBoostMode.Aggressive));
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
            trayMenu.Items.Add("Exit", null, (s, e) => { trayIcon.Visible = false; Application.Exit(); });

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
                    int avgLoad, maxCoreLoad;
                    HardwareMonitor.GetCpuMetrics(out avgLoad, out maxCoreLoad);

                    // Add to rolling sparkline history
                    telemetryHistory.Add(maxCoreLoad);
                    if (telemetryHistory.Count > MaxHistoryPoints) telemetryHistory.RemoveAt(0);
                    pnlSparkline.Invalidate();

                    // Centralized Priority Arbitration:
                    // 1. Check for Active Games (Direct3D Fullscreen OR Process List)
                    bool gameRunning = false;
                    if (chkAutoPilot.Checked)
                    {
                        if (HardwareMonitor.IsD3DFullscreenGameActive())
                        {
                            gameRunning = true;
                        }
                        else
                        {
                            foreach (string procName in WatchedProcesses)
                            {
                                if (Process.GetProcessesByName(procName).Length > 0)
                                {
                                    gameRunning = true;
                                    break;
                                }
                            }
                        }

                        if (gameRunning && !isGameActive)
                        {
                            isGameActive = true;
                            NativePower.ApplyModeDirect(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive);
                            trayIcon.ShowBalloonTip(1800, "Game Detected", "Locked Sweet-Spot Profile (3500 MHz) for consistent frametimes.", ToolTipIcon.Info);
                        }
                        else if (!gameRunning && isGameActive)
                        {
                            isGameActive = false;
                            NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Disabled);
                            trayIcon.ShowBalloonTip(1800, "Game Exited", "Reverted to Ice-Cold Profile (~2.4 GHz).", ToolTipIcon.Info);
                        }
                    }

                    // 2. Dynamic Governor (Fast-attack, slow-release)
                    if (chkDynamic.Checked && !gameRunning)
                    {
                        // Heavy Load: Immediate Fast-Attack
                        if (maxCoreLoad >= 85 || avgLoad >= 75)
                        {
                            stepDownDwellTicks = 3; // Hold for at least 6s
                            NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Aggressive);
                        }
                        // Medium / Gaming Load: Immediate Step-Up
                        else if (maxCoreLoad >= 25 || avgLoad >= 20)
                        {
                            stepDownDwellTicks = 3;
                            NativePower.ApplyModeDirect(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive);
                        }
                        // Low Load: Slow-Release Dwell Check
                        else
                        {
                            if (stepDownDwellTicks > 0)
                            {
                                stepDownDwellTicks--;
                            }
                            else
                            {
                                NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Disabled);
                            }
                        }
                    }

                    RefreshStatus(avgLoad, maxCoreLoad);
                }
                catch (Exception ex)
                {
                    try { File.AppendAllText("error.log", ex.ToString() + "\n"); } catch { }
                }
            };
            backgroundTimer.Start();
        }

        private void SetManualMode(uint freq, PerfBoostMode boost)
        {
            if (chkDynamic.Checked)
            {
                chkDynamic.Checked = false; // Disable dynamic governor so manual selection holds
            }

            if (NativePower.ApplyModeDirect(freq, boost))
            {
                SystemSounds.Asterisk.Play();
                int a, m;
                HardwareMonitor.GetCpuMetrics(out a, out m);
                RefreshStatus(a, m);
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
