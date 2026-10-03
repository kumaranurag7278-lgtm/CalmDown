using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Handle CLI flags if passed
            if (args != null && args.Length > 0)
            {
                string flag = args[0].Trim().ToLowerInvariant();
                switch (flag)
                {
                    case "--ice":
                    case "-i":
                        PowerHelper.ApplyMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Disabled, false);
                        return;
                    case "--sweet":
                    case "-s":
                        PowerHelper.ApplyMode(PowerHelper.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive, false);
                        return;
                    case "--beast":
                    case "-b":
                        PowerHelper.ApplyMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Aggressive, false);
                        return;
                    case "--restore":
                    case "-r":
                        PowerHelper.RestoreOriginal();
                        return;
                }
            }

            // Otherwise, launch GUI
            Application.Run(new MainForm());
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
                return ApplyMode(freq, (PerfBoostMode)boost, false);
            }
            catch { return false; }
        }

        public static bool ApplyMode(int freqMhz, PerfBoostMode boostMode, bool silent)
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
        private Panel pnlHeader;
        private Label lblTitle;
        private Label lblSubTitle;
        private Label lblActiveBadge;

        private Button btnCold;
        private Button btnSweet;
        private Button btnBeast;

        private CheckBox chkDynamicGovernor;
        private Label lblDynamicDesc;
        private CheckBox chkAutoPilot;
        private Label lblAutoPilotDesc;

        private Button btnRestore;
        private Button btnMinimizeTray;

        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem trayDynamicItem;
        private ToolStripMenuItem trayAutoPilotItem;
        private Timer backgroundTimer;

        // Auto-pilot watched games
        private static readonly string[] WatchedProcesses = new[]
        {
            "VALORANT", "VALORANT-Win64-Shipping", "cs2", "GTA5", "Overwatch", "FortniteClient-Win64-Shipping", "r5apex"
        };
        private bool isGameActive = false;

        // Governor debounce counters
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

        private void InitializeComponent()
        {
            this.Text = "CalmDown v2.0 - Thermal Governor";
            this.Size = new Size(520, 630);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.BackColor = Color.FromArgb(24, 26, 32); // Modern Dark Slate
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Header Banner
            pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(520, 95),
                BackColor = Color.FromArgb(18, 19, 24)
            };

            lblTitle = new Label
            {
                Text = "🧘 CalmDown",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 242, 245),
                Location = new Point(25, 16),
                AutoSize = true
            };

            lblSubTitle = new Label
            {
                Text = "Smart CPU Thermal & Performance Governor for Gaming Laptops",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(150, 155, 170),
                Location = new Point(28, 48),
                AutoSize = true
            };

            lblActiveBadge = new Label
            {
                Text = "MODE: CHECKING...",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(28, 68),
                AutoSize = true,
                ForeColor = Color.FromArgb(76, 175, 80)
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSubTitle);
            pnlHeader.Controls.Add(lblActiveBadge);
            this.Controls.Add(pnlHeader);

            // Preset Buttons
            btnCold = CreateStyledCard(
                "❄️  ICE-COLD MODE",
                "Locked Base Frequency (~2.4 GHz) | Zero Turbo Boost\nSilent fans, minimal power draw (~25W), keyboard stays cool.\n👉 Recommended for: Normal Study, Web Browsing, YouTube",
                new Point(25, 110),
                Color.FromArgb(21, 35, 54),
                Color.FromArgb(41, 121, 255),
                () => {
                    SetMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Disabled);
                }
            );

            btnSweet = CreateStyledCard(
                "⚖️  SWEET-SPOT BALANCED",
                "Capped at 3.5 GHz | Efficient Aggressive Governor\nDelivers ~90% peak FPS without the 90C+ exponential voltage spike.\n👉 Recommended for: Valorant, CS2, Competitive Gaming",
                new Point(25, 195),
                Color.FromArgb(20, 42, 32),
                Color.FromArgb(0, 200, 83),
                () => {
                    SetMode(PowerHelper.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive);
                }
            );

            btnBeast = CreateStyledCard(
                "🔥  BEAST TURBO",
                "Uncapped Clocks (Up to 4.9 GHz) | Stock Aggressive Profile\nMaximum single-core and multi-core wattage (High Thermals).\n👉 Recommended for: 4K Video Exports, Code Compiling, Benchmarks",
                new Point(25, 280),
                Color.FromArgb(46, 26, 28),
                Color.FromArgb(255, 82, 82),
                () => {
                    SetMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Aggressive);
                }
            );

            this.Controls.Add(btnCold);
            this.Controls.Add(btnSweet);
            this.Controls.Add(btnBeast);

            // Automation Settings Panel
            var pnlAuto = new Panel
            {
                Location = new Point(25, 370),
                Size = new Size(455, 140),
                BackColor = Color.FromArgb(32, 35, 45)
            };

            // 1. Dynamic Governor Toggle
            chkDynamicGovernor = new CheckBox
            {
                Text = "⚡  Smart Dynamic Governor (Auto-tune by CPU Load)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 235, 245),
                Location = new Point(14, 12),
                AutoSize = true,
                Checked = false, // User chooses ON or OFF
                Cursor = Cursors.Hand
            };
            chkDynamicGovernor.CheckedChanged += (s, e) =>
            {
                if (trayDynamicItem != null) trayDynamicItem.Checked = chkDynamicGovernor.Checked;
                if (!chkDynamicGovernor.Checked)
                {
                    trayIcon.ShowBalloonTip(1200, "Dynamic Governor", "Disabled. CalmDown will hold your chosen preset.", ToolTipIcon.Info);
                }
                else
                {
                    trayIcon.ShowBalloonTip(1200, "Dynamic Governor", "Enabled! CalmDown will auto-adjust clocks by CPU load.", ToolTipIcon.Info);
                }
            };

            lblDynamicDesc = new Label
            {
                Text = "< 20% Load -> Ice-Cold  |  25-80% -> Sweet-Spot  |  > 85% -> Beast Turbo",
                Font = new Font("Segoe UI", 8, FontStyle.Regular),
                ForeColor = Color.FromArgb(160, 165, 180),
                Location = new Point(34, 34),
                AutoSize = true
            };

            // 2. Game Auto-Pilot Toggle
            chkAutoPilot = new CheckBox
            {
                Text = "🎮  Game Auto-Pilot (Priority Sweet-Spot for Games)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 235, 245),
                Location = new Point(14, 72),
                AutoSize = true,
                Checked = true,
                Cursor = Cursors.Hand
            };
            chkAutoPilot.CheckedChanged += (s, e) =>
            {
                if (trayAutoPilotItem != null) trayAutoPilotItem.Checked = chkAutoPilot.Checked;
            };

            lblAutoPilotDesc = new Label
            {
                Text = "Locks 3.5 GHz when Valorant, CS2, GTA 5, or Apex starts for stutter-free FPS.",
                Font = new Font("Segoe UI", 8, FontStyle.Regular),
                ForeColor = Color.FromArgb(160, 165, 180),
                Location = new Point(34, 94),
                AutoSize = true
            };

            pnlAuto.Controls.Add(chkDynamicGovernor);
            pnlAuto.Controls.Add(lblDynamicDesc);
            pnlAuto.Controls.Add(chkAutoPilot);
            pnlAuto.Controls.Add(lblAutoPilotDesc);
            this.Controls.Add(pnlAuto);

            // Bottom Action Buttons
            btnRestore = new Button
            {
                Text = "🛡️ Reset to Stock",
                Location = new Point(25, 530),
                Size = new Size(160, 36),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                BackColor = Color.FromArgb(38, 41, 52),
                ForeColor = Color.FromArgb(180, 185, 200),
                Cursor = Cursors.Hand
            };
            btnRestore.FlatAppearance.BorderSize = 0;
            btnRestore.Click += (s, e) =>
            {
                chkDynamicGovernor.Checked = false;
                if (PowerHelper.RestoreOriginal())
                {
                    MessageBox.Show("Original stock power settings successfully restored!", "CalmDown", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshStatus(CpuMonitor.GetCurrentLoad());
                }
            };
            this.Controls.Add(btnRestore);

            btnMinimizeTray = new Button
            {
                Text = "📌 Minimize to Tray",
                Location = new Point(320, 530),
                Size = new Size(160, 36),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(41, 121, 255),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnMinimizeTray.FlatAppearance.BorderSize = 0;
            btnMinimizeTray.Click += (s, e) =>
            {
                this.Hide();
                trayIcon.ShowBalloonTip(1500, "CalmDown Active", "Minimized to tray. Monitoring in the background.", ToolTipIcon.Info);
            };
            this.Controls.Add(btnMinimizeTray);

            // Minimize on Form Close
            this.FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    this.Hide();
                    trayIcon.ShowBalloonTip(1200, "CalmDown", "Running quietly in system tray. Double-click icon to reopen.", ToolTipIcon.Info);
                }
            };
        }

        private Button CreateStyledCard(string title, string details, Point loc, Color bg, Color accent, Action onClick)
        {
            var btn = new Button
            {
                Location = loc,
                Size = new Size(455, 75),
                BackColor = bg,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(12, 10, 10, 8)
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(60, accent.R, accent.G, accent.B);

            var lblT = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = accent,
                Location = new Point(12, 8),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            lblT.Click += (s, e) => onClick();

            var lblD = new Label
            {
                Text = details,
                Font = new Font("Segoe UI", 7.8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(190, 195, 210),
                Location = new Point(12, 28),
                Size = new Size(430, 42),
                BackColor = Color.Transparent
            };
            lblD.Click += (s, e) => onClick();

            btn.Controls.Add(lblT);
            btn.Controls.Add(lblD);
            btn.Click += (s, e) => onClick();

            btn.MouseEnter += (s, e) => btn.FlatAppearance.BorderColor = accent;
            btn.MouseLeave += (s, e) => btn.FlatAppearance.BorderColor = Color.FromArgb(60, accent.R, accent.G, accent.B);

            return btn;
        }

        private void SetupTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("🧘 CalmDown v2.0", null, (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; });
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("❄️ Ice-Cold Mode", null, (s, e) => SetMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Disabled));
            trayMenu.Items.Add("⚖️ Sweet-Spot Mode", null, (s, e) => SetMode(PowerHelper.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive));
            trayMenu.Items.Add("🔥 Beast Turbo Mode", null, (s, e) => SetMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Aggressive));
            trayMenu.Items.Add("-");

            trayDynamicItem = new ToolStripMenuItem("⚡ Smart Dynamic Governor", null, (s, e) =>
            {
                chkDynamicGovernor.Checked = !chkDynamicGovernor.Checked;
            }) { CheckOnClick = false, Checked = chkDynamicGovernor.Checked };
            trayMenu.Items.Add(trayDynamicItem);

            trayAutoPilotItem = new ToolStripMenuItem("🎮 Game Auto-Pilot", null, (s, e) =>
            {
                chkAutoPilot.Checked = !chkAutoPilot.Checked;
            }) { CheckOnClick = false, Checked = chkAutoPilot.Checked };
            trayMenu.Items.Add(trayAutoPilotItem);

            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Open CalmDown", null, (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; });
            trayMenu.Items.Add("Exit CalmDown", null, (s, e) => { trayIcon.Visible = false; Application.Exit(); });

            trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Shield,
                ContextMenuStrip = trayMenu,
                Text = "CalmDown: Active",
                Visible = true
            };
            trayIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; };
        }

        private void SetupGovernorTimer()
        {
            backgroundTimer = new Timer { Interval = 2500 };
            backgroundTimer.Tick += (s, e) =>
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
                        PowerHelper.ApplyMode(PowerHelper.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive, true);
                        trayIcon.ShowBalloonTip(1800, "🎮 Game Detected", "CalmDown locked Sweet-Spot Mode (3.5 GHz) for smooth FPS.", ToolTipIcon.Info);
                    }
                    else if (!gameRunning && isGameActive)
                    {
                        isGameActive = false;
                        PowerHelper.ApplyMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Disabled, true);
                        trayIcon.ShowBalloonTip(1800, "❄️ Game Closed", "CalmDown returned to Ice-Cold Mode. Cooling down.", ToolTipIcon.Info);
                    }
                }

                // 2. Dynamic Governor (CPU load based) - only active if game is NOT overriding
                if (chkDynamicGovernor.Checked && !gameRunning)
                {
                    if (currentLoad >= 85)
                    {
                        heavyLoadCount++;
                        midLoadCount = 0;
                        lowLoadCount = 0;
                        if (heavyLoadCount >= 2) // Sustained heavy load (5s)
                        {
                            PowerHelper.ApplyMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Aggressive, true);
                        }
                    }
                    else if (currentLoad >= 25 && currentLoad < 85)
                    {
                        midLoadCount++;
                        heavyLoadCount = 0;
                        lowLoadCount = 0;
                        if (midLoadCount >= 2) // Sustained medium load (5s)
                        {
                            PowerHelper.ApplyMode(PowerHelper.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive, true);
                        }
                    }
                    else // < 20%
                    {
                        lowLoadCount++;
                        heavyLoadCount = 0;
                        midLoadCount = 0;
                        if (lowLoadCount >= 2) // Sustained low load (5s)
                        {
                            PowerHelper.ApplyMode(PowerHelper.FREQ_UNCAPPED, PerfBoostMode.Disabled, true);
                        }
                    }
                }

                RefreshStatus(currentLoad);
            };
            backgroundTimer.Start();
        }

        private void SetMode(int freq, PerfBoostMode boost)
        {
            // If user manually clicks, disable dynamic governor so manual selection holds
            if (chkDynamicGovernor.Checked)
            {
                chkDynamicGovernor.Checked = false;
            }

            if (PowerHelper.ApplyMode(freq, boost, false))
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
                lblActiveBadge.Text = "STATUS: ERROR READING SCHEME";
                lblActiveBadge.ForeColor = Color.Red;
                return;
            }

            int b = PowerHelper.ParseCurrentIndex(boostOut);
            int f = PowerHelper.ParseCurrentIndex(freqOut);

            string modeName;
            Color modeColor;

            if (b == (int)PerfBoostMode.Disabled)
            {
                modeName = "❄️ ICE-COLD (2.4 GHz | Zero Boost)";
                modeColor = Color.FromArgb(41, 121, 255);
            }
            else if (f == PowerHelper.FREQ_SWEETSPOT_MHZ)
            {
                modeName = "⚖️ SWEET-SPOT (3.5 GHz Capped)";
                modeColor = Color.FromArgb(0, 200, 83);
            }
            else if (b == (int)PerfBoostMode.Aggressive && f == PowerHelper.FREQ_UNCAPPED)
            {
                modeName = "🔥 BEAST TURBO (Uncapped 4.9 GHz)";
                modeColor = Color.FromArgb(255, 82, 82);
            }
            else
            {
                modeName = string.Format("CUSTOM (Boost={0}, Max={1}MHz)", b, f);
                modeColor = Color.FromArgb(255, 193, 7);
            }

            string autoTag = "";
            if (chkDynamicGovernor != null && chkDynamicGovernor.Checked) autoTag = " [DYNAMIC]";
            else if (isGameActive) autoTag = " [GAME PRIORITY]";

            lblActiveBadge.Text = string.Format("ACTIVE: {0}{1}  |  CPU: {2}%", modeName, autoTag, currentLoad);
            lblActiveBadge.ForeColor = modeColor;
            trayIcon.Text = string.Format("CalmDown: {0} ({1}%)", modeName.Length > 20 ? modeName.Substring(0, 18) + ".." : modeName, currentLoad);
        }
    }
}
