using System;
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
    internal enum PerfBoostMode
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

            // Handle CLI flags if passed
            if (args != null && args.Length > 0)
            {
                string flag = args[0].Trim().ToLowerInvariant();
                switch (flag)
                {
                    case "--ice":
                    case "-i":
                        PowerHelper.ApplyMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Disabled);
                        return;
                    case "--sweet":
                    case "-s":
                        PowerHelper.ApplyMode(PowerHelper.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive);
                        return;
                    case "--beast":
                    case "-b":
                        PowerHelper.ApplyMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Aggressive);
                        return;
                    case "--restore":
                    case "-r":
                        PowerHelper.RestoreOriginal();
                        return;
                }
            }

            // Enforce single instance: if already running, restore existing window and exit
            bool isFirstInstance;
            singleInstanceMutex = new Mutex(true, "CalmDown_SingleInstance_Mutex_Session", out isFirstInstance);
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
            // Default settings
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
                string content = string.Format(
                    "# CalmDown Hardware Configuration\r\nDynamicGovernor={0}\r\nGameAutoPilot={1}\r\n",
                    DynamicGovernor, GameAutoPilot);
                File.WriteAllText(ConfigFile, content);
            }
            catch { }
        }
    }

    internal static class CpuMonitor
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME idleTime,
                                                  out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime,
                                                  out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

        private static ulong prevIdle = 0;
        private static ulong prevKernel = 0;
        private static ulong prevUser = 0;
        private static bool initialized = false;

        private static ulong ToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME ft)
        {
            return ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
        }

        public static int GetCurrentLoad()
        {
            System.Runtime.InteropServices.ComTypes.FILETIME idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) return -1;

            ulong curIdle = ToUInt64(idle);
            ulong curKernel = ToUInt64(kernel);
            ulong curUser = ToUInt64(user);

            if (!initialized)
            {
                prevIdle = curIdle;
                prevKernel = curKernel;
                prevUser = curUser;
                initialized = true;
                return 0;
            }

            ulong diffIdle = curIdle - prevIdle;
            ulong diffKernel = curKernel - prevKernel;
            ulong diffUser = curUser - prevUser;

            prevIdle = curIdle;
            prevKernel = curKernel;
            prevUser = curUser;

            ulong sysTotal = diffKernel + diffUser;
            if (sysTotal == 0) return 0;

            if (diffIdle > sysTotal) diffIdle = sysTotal;
            ulong busy = sysTotal - diffIdle;
            int pct = (int)((busy * 100) / sysTotal);
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;
            return pct;
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

    internal static class PowerHelper
    {
        public const int FREQ_UNCAPPED = 0;
        public const int FREQ_SWEETSPOT_MHZ = 3500;
        private static readonly string BackupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CalmDown", "original_settings.txt");

        private static int currentAppliedFreq = -999;
        private static int currentAppliedBoost = -999;

        public static bool RunPowercfg(string args, out string output)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo("powercfg", args)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return false;
            }
        }

        public static int ParseCurrentIndex(string powercfgOutput)
        {
            foreach (string rawLine in powercfgOutput.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("Current AC Power Setting Index:", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split(':');
                    if (parts.Length > 1)
                    {
                        try { return Convert.ToInt32(parts[1].Trim(), 16); }
                        catch { return -1; }
                    }
                }
            }
            return -1;
        }

        public static void EnsureBackup()
        {
            try
            {
                if (File.Exists(BackupPath)) return;
                string dir = Path.GetDirectoryName(BackupPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string boostOut, freqOut;
                RunPowercfg("/qh SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE", out boostOut);
                RunPowercfg("/qh SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX", out freqOut);

                int b = ParseCurrentIndex(boostOut);
                int f = ParseCurrentIndex(freqOut);
                File.WriteAllText(BackupPath, string.Format("{0},{1}", b, f));
            }
            catch { }
        }

        public static bool RestoreOriginal()
        {
            try
            {
                if (!File.Exists(BackupPath)) return false;
                string[] parts = File.ReadAllText(BackupPath).Trim().Split(',');
                if (parts.Length < 2) return false;

                int boost = int.Parse(parts[0]);
                int freq = int.Parse(parts[1]);
                if (boost < 0) boost = 2;
                if (freq < 0) freq = 0;

                currentAppliedFreq = -999;
                currentAppliedBoost = -999;
                return ApplyMode(freq, (PerfBoostMode)boost);
            }
            catch { return false; }
        }

        public static bool ApplyMode(int freqMhz, PerfBoostMode boostMode)
        {
            if (currentAppliedFreq == freqMhz && currentAppliedBoost == (int)boostMode)
            {
                return true;
            }

            EnsureBackup();
            string dummy;
            bool ok = true;
            foreach (string kind in new[] { "/setacvalueindex", "/setdcvalueindex" })
            {
                if (!RunPowercfg(string.Format("{0} SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX {1}", kind, freqMhz), out dummy) ||
                    !RunPowercfg(string.Format("{0} SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {1}", kind, (int)boostMode), out dummy))
                {
                    ok = false;
                }
            }
            if (ok)
            {
                RunPowercfg("/setactive SCHEME_CURRENT", out dummy);
                currentAppliedFreq = freqMhz;
                currentAppliedBoost = (int)boostMode;
            }
            return ok;
        }
    }

    public class MainForm : Form
    {
        private Panel pnlHardware;
        private Label lblCpuName;
        private Label lblAcpiTarget;
        private Label lblLiveTelemetry;
        private ProgressBar prgCpuLoad;

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

        private static readonly string[] WatchedProcesses = new[]
        {
            "VALORANT", "VALORANT-Win64-Shipping", "cs2", "GTA5", "Overwatch", "FortniteClient-Win64-Shipping", "r5apex"
        };
        private bool isGameActive = false;

        private int lowLoadCount = 0;
        private int midLoadCount = 0;
        private int heavyLoadCount = 0;

        public MainForm()
        {
            InitializeComponent();
            SetupTray();
            SetupGovernorTimer();
            RefreshStatus(0);
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
            this.Text = "CalmDown - Hardware Power Governor";
            this.Size = new Size(570, 620);
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
                Size = new Size(518, 86),
                BackColor = Color.FromArgb(25, 27, 33)
            };
            pnlHardware.Paint += (s, e) => {
                ControlPaint.DrawBorder(e.Graphics, pnlHardware.ClientRectangle,
                    Color.FromArgb(42, 45, 54), ButtonBorderStyle.Solid);
            };

            lblCpuName = new Label
            {
                Text = "CPU: " + CpuMonitor.GetProcessorName(),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(245, 247, 252),
                Location = new Point(14, 10),
                AutoSize = true
            };

            lblAcpiTarget = new Label
            {
                Text = "ACPI Target: SCHEME_CURRENT -> SUB_PROCESSOR (PROCFREQMAX / PERFBOOSTMODE)",
                Font = new Font("Consolas", 7.8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(145, 152, 168),
                Location = new Point(15, 33),
                AutoSize = true
            };

            lblLiveTelemetry = new Label
            {
                Text = "STATUS: INITIALIZING...",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 200, 83),
                Location = new Point(14, 56),
                AutoSize = true
            };

            prgCpuLoad = new ProgressBar
            {
                Location = new Point(380, 57),
                Size = new Size(120, 16),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            pnlHardware.Controls.Add(lblCpuName);
            pnlHardware.Controls.Add(lblAcpiTarget);
            pnlHardware.Controls.Add(lblLiveTelemetry);
            pnlHardware.Controls.Add(prgCpuLoad);
            this.Controls.Add(pnlHardware);

            // 2. Profile Selection Cards
            cardIce = CreateProfileCard(
                "ICE-COLD PROFILE",
                "Frequency Ceiling: Base Clock (~2.4 GHz)  |  Boost: Disabled (0x0)\nTarget Wattage: ~15W–25W  |  Keyboard Cool  |  Fans: Silent\nOptimal for: Background Study, Web Browsing, Document Work",
                new Point(18, 112),
                Color.FromArgb(0, 168, 255),
                out lblIceTag,
                () => SetManualMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Disabled)
            );

            cardSweet = CreateProfileCard(
                "SWEET-SPOT BALANCED PROFILE",
                "Frequency Ceiling: 3500 MHz  |  Boost: Efficient Aggressive (0x4)\nTarget Wattage: ~30W–35W  |  Curb Voltage Runaway  |  Stable Frametimes\nOptimal for: Valorant, CS2, Competitive Gaming, Daily Multitasking",
                new Point(18, 192),
                Color.FromArgb(0, 200, 83),
                out lblSweetTag,
                () => SetManualMode(PowerHelper.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive)
            );

            cardBeast = CreateProfileCard(
                "BEAST TURBO PROFILE",
                "Frequency Ceiling: Uncapped (Up to 4.9 GHz)  |  Boost: Aggressive (0x2)\nTarget Wattage: Full TDP (~65W–75W+)  |  High Thermals\nOptimal for: Video Rendering, Code Compiling, Benchmarking",
                new Point(18, 272),
                Color.FromArgb(255, 82, 82),
                out lblBeastTag,
                () => SetManualMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Aggressive)
            );

            this.Controls.Add(cardIce);
            this.Controls.Add(cardSweet);
            this.Controls.Add(cardBeast);

            // 3. Autonomous Automation Settings Panel
            pnlAutomation = new Panel
            {
                Location = new Point(18, 362),
                Size = new Size(518, 118),
                BackColor = Color.FromArgb(25, 27, 33)
            };
            pnlAutomation.Paint += (s, e) => {
                ControlPaint.DrawBorder(e.Graphics, pnlAutomation.ClientRectangle,
                    Color.FromArgb(42, 45, 54), ButtonBorderStyle.Solid);
            };

            chkDynamic = new CheckBox
            {
                Text = "Enable Smart Dynamic Load Governor",
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
                RefreshStatus(CpuMonitor.GetCurrentLoad());
            };

            lblDynamicSub = new Label
            {
                Text = "Dynamically adjusts frequency ceilings based on sustained CPU demand\n(<20% Load: Ice-Cold  |  25-80% Load: Sweet-Spot  |  >85% Load: Beast Turbo)",
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(145, 152, 168),
                Location = new Point(34, 32),
                Size = new Size(470, 28)
            };

            chkAutoPilot = new CheckBox
            {
                Text = "Enable Game Auto-Pilot Priority",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(235, 238, 248),
                Location = new Point(14, 66),
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
                Text = "Locks Sweet-Spot (3500 MHz) when games run to guarantee zero frametime drops.",
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(145, 152, 168),
                Location = new Point(34, 88),
                Size = new Size(470, 18)
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
                Location = new Point(18, 492),
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
                if (PowerHelper.RestoreOriginal())
                {
                    MessageBox.Show("Stock ACPI power scheme indices successfully restored.", "CalmDown", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshStatus(CpuMonitor.GetCurrentLoad());
                }
            };
            this.Controls.Add(btnRestore);

            btnMinimizeTray = new Button
            {
                Text = "Minimize to System Tray",
                Location = new Point(350, 492),
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
                trayIcon.ShowBalloonTip(1500, "CalmDown", "Running quietly in tray. Double-click tray icon to restore.", ToolTipIcon.Info);
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
                Text = "Ready  |  ACPI Status: OK  |  Single Instance: Active",
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

        private Panel CreateProfileCard(string title, string specs, Point loc, Color accent, out Label outTag, Action onClick)
        {
            var pnl = new Panel
            {
                Location = loc,
                Size = new Size(518, 72),
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
                Location = new Point(410, 10),
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
                Size = new Size(490, 38),
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
            trayMenu.Items.Add("CalmDown Hardware Governor", null, (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; });
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Ice-Cold Profile (2.4 GHz)", null, (s, e) => SetManualMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Disabled));
            trayMenu.Items.Add("Sweet-Spot Profile (3.5 GHz)", null, (s, e) => SetManualMode(PowerHelper.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive));
            trayMenu.Items.Add("Beast Turbo Profile (Uncapped)", null, (s, e) => SetManualMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Aggressive));
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
            backgroundTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            backgroundTimer.Tick += (s, e) =>
            {
                try
                {
                    int currentLoad = CpuMonitor.GetCurrentLoad();
                    if (currentLoad < 0) currentLoad = 0;

                    // 1. Check for Active Games
                    bool gameRunning = false;
                    if (chkAutoPilot.Checked)
                    {
                        foreach (string procName in WatchedProcesses)
                        {
                            if (Process.GetProcessesByName(procName).Length > 0)
                            {
                                gameRunning = true;
                                break;
                            }
                        }

                        if (gameRunning && !isGameActive)
                        {
                            isGameActive = true;
                            PowerHelper.ApplyMode(PowerHelper.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive);
                            trayIcon.ShowBalloonTip(1800, "Game Detected", "Locked Sweet-Spot Profile (3500 MHz) for consistent frametimes.", ToolTipIcon.Info);
                        }
                        else if (!gameRunning && isGameActive)
                        {
                            isGameActive = false;
                            PowerHelper.ApplyMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Disabled);
                            trayIcon.ShowBalloonTip(1800, "Game Exited", "Reverted to Ice-Cold Profile (~2.4 GHz).", ToolTipIcon.Info);
                        }
                    }

                    // 2. Dynamic Governor (CPU load based)
                    if (chkDynamic.Checked && !gameRunning)
                    {
                        if (currentLoad >= 85)
                        {
                            heavyLoadCount++;
                            midLoadCount = 0;
                            lowLoadCount = 0;
                            if (heavyLoadCount >= 2)
                            {
                                PowerHelper.ApplyMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Aggressive);
                            }
                        }
                        else if (currentLoad >= 25 && currentLoad < 85)
                        {
                            midLoadCount++;
                            heavyLoadCount = 0;
                            lowLoadCount = 0;
                            if (midLoadCount >= 2)
                            {
                                PowerHelper.ApplyMode(PowerHelper.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive);
                            }
                        }
                        else // < 20%
                        {
                            lowLoadCount++;
                            heavyLoadCount = 0;
                            midLoadCount = 0;
                            if (lowLoadCount >= 2)
                            {
                                PowerHelper.ApplyMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Disabled);
                            }
                        }
                    }

                    RefreshStatus(currentLoad);
                }
                catch (Exception ex)
                {
                    try { File.AppendAllText("error.log", ex.ToString() + "\n"); } catch { }
                }
            };
            backgroundTimer.Start();
        }

        private void SetManualMode(int freq, PerfBoostMode boost)
        {
            if (chkDynamic.Checked)
            {
                chkDynamic.Checked = false; // Disable dynamic governor so manual selection holds
            }

            if (PowerHelper.ApplyMode(freq, boost))
            {
                SystemSounds.Asterisk.Play();
                RefreshStatus(CpuMonitor.GetCurrentLoad());
            }
        }

        private void RefreshStatus(int currentLoad)
        {
            string boostOut, freqOut;
            if (!PowerHelper.RunPowercfg("/qh SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE", out boostOut) ||
                !PowerHelper.RunPowercfg("/qh SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX", out freqOut))
            {
                lblLiveTelemetry.Text = "ACPI READ ERROR";
                lblLiveTelemetry.ForeColor = Color.Red;
                return;
            }

            int b = PowerHelper.ParseCurrentIndex(boostOut);
            int f = PowerHelper.ParseCurrentIndex(freqOut);

            bool isIce = (b == (int)PerfBoostMode.Disabled);
            bool isSweet = (f == PowerHelper.FREQ_SWEETSPOT_MHZ);
            bool isBeast = (b == (int)PerfBoostMode.Aggressive && f == PowerHelper.FREQ_UNCAPPED);

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

            lblLiveTelemetry.Text = string.Format("ACTIVE: {0}{1}  |  CPU LOAD: {2}%", modeTitle.ToUpperInvariant(), modeTag, currentLoad);
            lblLiveTelemetry.ForeColor = modeColor;

            if (prgCpuLoad != null)
            {
                prgCpuLoad.Value = Math.Max(0, Math.Min(100, currentLoad));
            }

            try
            {
                string trayStr = string.Format("CalmDown: {0}% ({1})", currentLoad, modeTitle);
                if (trayStr.Length > 63) trayStr = trayStr.Substring(0, 60) + "...";
                trayIcon.Text = trayStr;
            }
            catch { }
        }
    }
}
