using System;
using System.Diagnostics;
using System.Drawing;
using System.Management;
using System.Media;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CalmDown
{
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
        private Button btnCold;
        private Button btnSweet;
        private Button btnBeast;
        private Label lblFooter;
        private string activeMode = "Beast Turbo";

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

            // Title / Status Label
            lblStatus = new Label();
            lblStatus.Location = new Point(25, 18);
            lblStatus.Size = new Size(425, 30);
            lblStatus.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            lblStatus.ForeColor = Color.FromArgb(33, 37, 41);
            lblStatus.Text = "Detecting current mode...";
            this.Controls.Add(lblStatus);

            // Button 1: Ice-Cold Mode
            btnCold = new Button();
            btnCold.Location = new Point(25, 60);
            btnCold.Size = new Size(425, 75);
            btnCold.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            btnCold.BackColor = Color.FromArgb(227, 242, 253);
            btnCold.ForeColor = Color.FromArgb(13, 71, 161);
            btnCold.FlatStyle = FlatStyle.Flat;
            btnCold.FlatAppearance.BorderSize = 1;
            btnCold.FlatAppearance.BorderColor = Color.FromArgb(187, 222, 251);
            btnCold.Text = "1. ICE-COLD MODE  [Base Clock ~2.4 GHz | ~60C - 65C]\nSilent Fans - Zero Heating - Long Battery Life\n(Best for: Normal Study, Web Browsing, Daily Tasks)";
            btnCold.Cursor = Cursors.Hand;
            btnCold.Click += (s, e) => SetIceCold();
            this.Controls.Add(btnCold);

            // Button 2: Sweet-Spot Balanced Mode
            btnSweet = new Button();
            btnSweet.Location = new Point(25, 145);
            btnSweet.Size = new Size(425, 75);
            btnSweet.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            btnSweet.BackColor = Color.FromArgb(232, 245, 233);
            btnSweet.ForeColor = Color.FromArgb(27, 94, 32);
            btnSweet.FlatStyle = FlatStyle.Flat;
            btnSweet.FlatAppearance.BorderSize = 1;
            btnSweet.FlatAppearance.BorderColor = Color.FromArgb(200, 230, 201);
            btnSweet.Text = "2. SWEET-SPOT BALANCED  [Capped 3.5 GHz | ~72C - 78C]\nHigh FPS - No Thermal Throttling - Controlled Heat\n(Best for: Valorant, Competitive Gaming, Multitasking)";
            btnSweet.Cursor = Cursors.Hand;
            btnSweet.Click += (s, e) => SetSweetSpot();
            this.Controls.Add(btnSweet);

            // Button 3: Beast Turbo Mode
            btnBeast = new Button();
            btnBeast.Location = new Point(25, 230);
            btnBeast.Size = new Size(425, 75);
            btnBeast.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            btnBeast.BackColor = Color.FromArgb(255, 235, 238);
            btnBeast.ForeColor = Color.FromArgb(183, 28, 28);
            btnBeast.FlatStyle = FlatStyle.Flat;
            btnBeast.FlatAppearance.BorderSize = 1;
            btnBeast.FlatAppearance.BorderColor = Color.FromArgb(255, 205, 210);
            btnBeast.Text = "3. BEAST TURBO  [Max Uncapped 4.9 GHz | Full Power]\n100% Uncapped Speed - Max 115W Boost - Peak Clocks\n(Best for: 4K Video Exports, Code Compiling, Heavy Work)";
            btnBeast.Cursor = Cursors.Hand;
            btnBeast.Click += (s, e) => SetBeastTurbo();
            this.Controls.Add(btnBeast);

            // Footer Label
            lblFooter = new Label();
            lblFooter.Location = new Point(25, 320);
            lblFooter.Size = new Size(425, 45);
            lblFooter.Font = new Font("Segoe UI", 8, FontStyle.Regular);
            lblFooter.ForeColor = Color.Gray;
            lblFooter.Text = "CalmDown v1.1.0 by Anurag Kumar | Open-Source on GitHub\nApplies changes dynamically to the active Windows power scheme.";
            this.Controls.Add(lblFooter);
        }

        private string RunPowercfg(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powercfg", args)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return output;
                }
            }
            catch
            {
                return "";
            }
        }

        private void DetectCurrentMode()
        {
            try
            {
                string boostOut = RunPowercfg("/qh SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE");
                string freqOut = RunPowercfg("/qh SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX");

                bool boostDisabled = boostOut.Contains("0x00000000");
                bool freqCapped = freqOut.Contains("0x00000dac");

                if (boostDisabled)
                {
                    activeMode = "ICE-COLD (2.4 GHz)";
                }
                else if (freqCapped)
                {
                    activeMode = "SWEET-SPOT (3.5 GHz)";
                }
                else
                {
                    activeMode = "BEAST TURBO (Uncapped)";
                }

                lblStatus.Text = "Current Active Mode: " + activeMode;
            }
            catch
            {
                lblStatus.Text = "Current Active Mode: UNKNOWN";
            }
        }

        private void SetIceCold()
        {
            RunPowercfg("/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0");
            RunPowercfg("/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0");
            RunPowercfg("/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0");
            RunPowercfg("/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0");
            RunPowercfg("/setactive SCHEME_CURRENT");

            SystemSounds.Asterisk.Play();
            MessageBox.Show(
                "ICE-COLD MODE ACTIVATED!\n\n" +
                "- CPU Clock: Base Frequency (~2.4 GHz)\n" +
                "- Thermals: ~60C - 65C\n" +
                "- Fans: Silent & Keyboard stays cool\n" +
                "- Best for: Study, Browsing, Daily Work, Battery Saving",
                "CalmDown",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            this.Close();
        }

        private void SetSweetSpot()
        {
            RunPowercfg("/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 3500");
            RunPowercfg("/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 3500");
            RunPowercfg("/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 4");
            RunPowercfg("/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 4");
            RunPowercfg("/setactive SCHEME_CURRENT");

            SystemSounds.Asterisk.Play();
            MessageBox.Show(
                "SWEET-SPOT BALANCED MODE ACTIVATED!\n\n" +
                "- CPU Clock: Capped at 3.5 GHz (Sweet Spot)\n" +
                "- Thermals: ~72C - 78C\n" +
                "- High FPS without 95C overheating!\n" +
                "- Best for: Valorant, Competitive Gaming, Multitasking",
                "CalmDown",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            this.Close();
        }

        private void SetBeastTurbo()
        {
            RunPowercfg("/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0");
            RunPowercfg("/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0");
            RunPowercfg("/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 2");
            RunPowercfg("/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 2");
            RunPowercfg("/setactive SCHEME_CURRENT");

            SystemSounds.Asterisk.Play();
            MessageBox.Show(
                "BEAST TURBO MODE ACTIVATED!\n\n" +
                "- CPU Clock: Full Uncapped Turbo Boost (Up to 4.9 GHz)\n" +
                "- Power: Max Wattage (High Thermals)\n" +
                "- Best for: 4K Video Exports, Code Compiling, Heavy Benchmarks",
                "CalmDown",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            this.Close();
        }
    }
}
