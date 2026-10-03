using System;
using System.Diagnostics;
using System.Drawing;
using System.Media;
using System.Windows.Forms;

namespace CalmDown
{
    // Windows ACPI Processor Performance Boost Modes
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
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private Label lblStatus;
        private Label lblFooter;
        private string activeMode = "Unknown";

        // Preset configuration constants
        private const int FREQ_UNCAPPED = 0;
        private const int FREQ_SWEETSPOT_MHZ = 3500;

        public MainForm()
        {
            InitializeComponent();
            DetectCurrentMode();
        }

        private void InitializeComponent()
        {
            this.Text = "CalmDown - Thermal & Performance Switcher";
            this.Size = new Size(490, 420);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.TopMost = true;
            this.BackColor = Color.FromArgb(248, 249, 250);

            // Title / Status Header
            lblStatus = new Label
            {
                Location = new Point(25, 18),
                Size = new Size(425, 30),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41),
                Text = "Detecting active power scheme..."
            };
            this.Controls.Add(lblStatus);

            // Mode Buttons using clean helper
            var btnCold = CreateModeButton(
                "1. ICE-COLD MODE  [Base Frequency | Zero Boost]\nDisables Turbo Boost - Lowest Power & Voltage\n(Best for: Normal Study, Web Browsing, Maximum Battery)",
                new Point(25, 60),
                Color.FromArgb(227, 242, 253),
                Color.FromArgb(13, 71, 161),
                Color.FromArgb(187, 222, 251),
                () => ApplyMode(FREQ_UNCAPPED, PerfBoostMode.Disabled,
                    "ICE-COLD MODE ACTIVATED!",
                    "- Turbo Boost: DISABLED (Locked to hardware base clock)\n" +
                    "- Power: Lowest voltage & minimal thermal output\n" +
                    "- Best for: Everyday study, browsing, long battery life",
                    MessageBoxIcon.Information)
            );

            var btnSweet = CreateModeButton(
                "2. SWEET-SPOT BALANCED  [Capped 3.5 GHz | Efficient]\nHigh Clocks without Thermal Throttling Spikes\n(Best for: Valorant, Competitive Gaming, Multitasking)",
                new Point(25, 145),
                Color.FromArgb(232, 245, 233),
                Color.FromArgb(27, 94, 32),
                Color.FromArgb(200, 230, 201),
                () => ApplyMode(FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive,
                    "SWEET-SPOT BALANCED MODE ACTIVATED!",
                    "- Max Frequency: Capped at 3.5 GHz (Sweet Spot)\n" +
                    "- Boost Governor: Efficient Aggressive\n" +
                    "- Best for: Valorant, competitive gaming, reduced heat",
                    MessageBoxIcon.Information)
            );

            var btnBeast = CreateModeButton(
                "3. BEAST TURBO  [Max Uncapped Clocks | Full Power]\n100% Boost Headroom - Stock Aggressive Settings\n(Best for: 4K Video Exports, Code Compiling, Heavy Work)",
                new Point(25, 230),
                Color.FromArgb(255, 235, 238),
                Color.FromArgb(183, 28, 28),
                Color.FromArgb(255, 205, 210),
                () => ApplyMode(FREQ_UNCAPPED, PerfBoostMode.Aggressive,
                    "BEAST TURBO MODE ACTIVATED!",
                    "- Turbo Boost: UNCAPPED (Up to maximum hardware boost)\n" +
                    "- Governor: Stock Aggressive profile\n" +
                    "- Best for: Heavy rendering, compilation, benchmarks",
                    MessageBoxIcon.Warning)
            );

            this.Controls.Add(btnCold);
            this.Controls.Add(btnSweet);
            this.Controls.Add(btnBeast);

            // Footer
            lblFooter = new Label
            {
                Location = new Point(25, 320),
                Size = new Size(425, 45),
                Font = new Font("Segoe UI", 8, FontStyle.Regular),
                ForeColor = Color.Gray,
                Text = "CalmDown v1.1.1 by Anurag Kumar | Open-Source on GitHub\nApplies changes dynamically to the active Windows power scheme."
            };
            this.Controls.Add(lblFooter);
        }

        private Button CreateModeButton(string text, Point location, Color bg, Color fg, Color border, Action onClick)
        {
            var btn = new Button
            {
                Text = text,
                Location = location,
                Size = new Size(425, 75),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = bg,
                ForeColor = fg,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = border;
            btn.Click += (s, e) => onClick();
            return btn;
        }

        private bool RunPowercfg(string args, out string output)
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

        private void DetectCurrentMode()
        {
            string boostOut, freqOut;
            if (!RunPowercfg("/qh SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE", out boostOut) ||
                !RunPowercfg("/qh SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX", out freqOut))
            {
                lblStatus.Text = "Current Active Mode: Error querying powercfg";
                return;
            }

            int boostIndex = ParseCurrentIndex(boostOut);
            int freqIndex = ParseCurrentIndex(freqOut);

            if (boostIndex == (int)PerfBoostMode.Disabled)
            {
                activeMode = "ICE-COLD (Base Clock, Boost Disabled)";
            }
            else if (freqIndex == FREQ_SWEETSPOT_MHZ)
            {
                activeMode = "SWEET-SPOT (Capped 3.5 GHz)";
            }
            else if (boostIndex == (int)PerfBoostMode.Aggressive && freqIndex == FREQ_UNCAPPED)
            {
                activeMode = "BEAST TURBO (Uncapped Stock)";
            }
            else
            {
                activeMode = string.Format("Custom / Unmanaged (Boost={0}, Max={1}MHz)", boostIndex, freqIndex);
            }

            lblStatus.Text = "Current Active Mode: " + activeMode;
        }

        private int ParseCurrentIndex(string powercfgOutput)
        {
            foreach (string rawLine in powercfgOutput.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("Current AC Power Setting Index:", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split(':');
                    if (parts.Length > 1)
                    {
                        string hexStr = parts[1].Trim();
                        try
                        {
                            return Convert.ToInt32(hexStr, 16);
                        }
                        catch
                        {
                            return -1;
                        }
                    }
                }
            }
            return -1;
        }

        private void ApplyMode(int freqMhz, PerfBoostMode boostMode, string title, string details, MessageBoxIcon icon)
        {
            string outMsg;
            bool success = true;

            foreach (string kind in new[] { "/setacvalueindex", "/setdcvalueindex" })
            {
                if (!RunPowercfg(string.Format("{0} SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX {1}", kind, freqMhz), out outMsg) ||
                    !RunPowercfg(string.Format("{0} SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {1}", kind, (int)boostMode), out outMsg))
                {
                    success = false;
                }
            }

            if (success)
            {
                RunPowercfg("/setactive SCHEME_CURRENT", out outMsg);
                SystemSounds.Asterisk.Play();
                MessageBox.Show(title + "\n\n" + details, "CalmDown", MessageBoxButtons.OK, icon);
                this.Close();
            }
            else
            {
                SystemSounds.Hand.Play();
                MessageBox.Show("Failed to apply power setting via powercfg.\nPlease ensure you have permission to modify active power schemes.",
                    "CalmDown Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
