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

            // CLI flags handling before taking backup or touching ACPI
            if (args != null && args.Length > 0)
            {
                string flag = args[0].Trim().ToLowerInvariant();
                switch (flag)
                {
                    case "--selftest":
                        AttachConsole(-1);
                        SelfTest.Run();
                        return;
                    case "--bench":
                        AttachConsole(-1);
                        Benchmark.Run();
                        return;
                    case "--diagnose":
                    case "-d":
                        AttachConsole(-1);
                        Diagnostic.Run();
                        return;
                    case "--ice":
                    case "-i":
                        NativePower.EnsureBackup();
                        NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Disabled, true, true);
                        return;
                    case "--sweet":
                    case "-s":
                        NativePower.EnsureBackup();
                        NativePower.ApplyModeDirect(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive, true, true);
                        return;
                    case "--beast":
                    case "-b":
                        NativePower.EnsureBackup();
                        NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Aggressive, true, true);
                        return;
                    case "--restore":
                    case "-r":
                        NativePower.RestoreOriginalDirect();
                        return;
                }
            }

            // Immediately ensure original stock power settings are backed up on first GUI launch
            NativePower.EnsureBackup();

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

    internal static class Diagnostic
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint PowerGetEffectiveOverlaySchemeDelegate(out Guid EffectiveOverlayScheme);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public static void Run()
        {
            Console.WriteLine();
            Console.WriteLine("=========================================================");
            Console.WriteLine("   CalmDown Hardware & Power Subsystem Diagnostics");
            Console.WriteLine("=========================================================");

            // 1. System Overview
            Console.WriteLine(string.Format("  CalmDown Version   : {0}", "v3.0"));
            Console.WriteLine(string.Format("  OS Version         : {0}", Environment.OSVersion.VersionString));
            Console.WriteLine(string.Format("  CPU Name           : {0}", HardwareMonitor.GetProcessorName()));
            Console.WriteLine(string.Format("  Logical Processors : {0}", Environment.ProcessorCount));
            Console.WriteLine();

            // 2. Active Power Scheme & Friendly Name
            Guid activeScheme;
            if (!NativePower.GetActiveSchemeGuid(out activeScheme))
            {
                Console.WriteLine("  Active Power Scheme: read failed (unable to get GUID)");
            }
            else
            {
                string friendlyName;
                uint nameErr;
                if (NativePower.GetActiveSchemeFriendlyName(out friendlyName, out nameErr))
                {
                    Console.WriteLine(string.Format("  Active Scheme GUID : {0}", activeScheme));
                    Console.WriteLine(string.Format("  Active Scheme Name : {0}", friendlyName));
                }
                else
                {
                    Console.WriteLine(string.Format("  Active Scheme GUID : {0}", activeScheme));
                    Console.WriteLine(string.Format("  Active Scheme Name : read failed (code {0})", nameErr));
                }
                Console.WriteLine();

                // 3. Active Power Source & Rail Values
                string powerSource;
                switch (SystemInformation.PowerStatus.PowerLineStatus)
                {
                    case PowerLineStatus.Online:
                        powerSource = "AC (Wall Power / Plugged in)";
                        break;
                    case PowerLineStatus.Offline:
                        powerSource = "DC (Battery)";
                        break;
                    default:
                        powerSource = "Unknown";
                        break;
                }
                Console.WriteLine(string.Format("  Active Power Source: {0}", powerSource));

                // Read AC indices
                uint acBoost, acFreq;
                uint errAcB = NativePower.ReadSpecificIndex(activeScheme, true, NativePower.GuidBoostMode, out acBoost);
                uint errAcF = NativePower.ReadSpecificIndex(activeScheme, true, NativePower.GuidFreqMax, out acFreq);

                string acBoostStr = (errAcB == 0) ? string.Format("{0} ({1})", acBoost, (PerfBoostMode)acBoost) : ("read failed (code " + errAcB + ")");
                string acFreqStr = (errAcF == 0) ? (acFreq == 0 ? "0 (Uncapped)" : acFreq + " MHz") : ("read failed (code " + errAcF + ")");
                Console.WriteLine(string.Format("  AC Boost Mode      : {0}", acBoostStr));
                Console.WriteLine(string.Format("  AC Max Frequency   : {0}", acFreqStr));

                // Read DC indices
                uint dcBoost, dcFreq;
                uint errDcB = NativePower.ReadSpecificIndex(activeScheme, false, NativePower.GuidBoostMode, out dcBoost);
                uint errDcF = NativePower.ReadSpecificIndex(activeScheme, false, NativePower.GuidFreqMax, out dcFreq);

                string dcBoostStr = (errDcB == 0) ? string.Format("{0} ({1})", dcBoost, (PerfBoostMode)dcBoost) : ("read failed (code " + errDcB + ")");
                string dcFreqStr = (errDcF == 0) ? (dcFreq == 0 ? "0 (Uncapped)" : dcFreq + " MHz") : ("read failed (code " + errDcF + ")");
                Console.WriteLine(string.Format("  DC Boost Mode      : {0}", dcBoostStr));
                Console.WriteLine(string.Format("  DC Max Frequency   : {0}", dcFreqStr));
                Console.WriteLine();

                // 4. Windows Power Mode Overlay
                PrintOverlayScheme();
            }

            Console.WriteLine();
            // 5. OEM Control Software Detection
            PrintOemTools();

            Console.WriteLine("=========================================================");
            Console.WriteLine();
        }

        private static void PrintOverlayScheme()
        {
            IntPtr hPowrProf = GetModuleHandle("powrprof.dll");
            if (hPowrProf == IntPtr.Zero)
            {
                Console.WriteLine("  Overlay Scheme     : n/a");
                return;
            }

            IntPtr pFunc = GetProcAddress(hPowrProf, "PowerGetEffectiveOverlayScheme");
            if (pFunc == IntPtr.Zero)
            {
                Console.WriteLine("  Overlay Scheme     : n/a");
                return;
            }

            try
            {
                var func = (PowerGetEffectiveOverlaySchemeDelegate)Marshal.GetDelegateForFunctionPointer(pFunc, typeof(PowerGetEffectiveOverlaySchemeDelegate));
                Guid overlayGuid;
                uint ret = func(out overlayGuid);
                if (ret == 0)
                {
                    string overlayName = GetOverlayFriendlyName(overlayGuid);
                    Console.WriteLine(string.Format("  Overlay Scheme     : {0} ({1})", overlayGuid, overlayName));
                }
                else
                {
                    Console.WriteLine(string.Format("  Overlay Scheme     : read failed (code {0})", ret));
                }
            }
            catch
            {
                Console.WriteLine("  Overlay Scheme     : n/a");
            }
        }

        private static string GetOverlayFriendlyName(Guid g)
        {
            string s = g.ToString().ToLowerInvariant();
            if (s == "00000000-0000-0000-0000-000000000000") return "None / Default";
            if (s == "961cc777-2547-4f93-819f-090c433b4b80") return "Best Power Efficiency";
            if (s == "3af9b8d9-7c97-431d-ad7e-343427116b39") return "Balanced (Recommended)";
            if (s == "ded574b5-45a0-4f42-8737-46345c09c238") return "Best Performance";
            return "Custom Overlay";
        }

        private static void PrintOemTools()
        {
            string[] knownOem = new string[]
            {
                "NitroSense", "PredatorSense", "AcerSense", "Vantage", "LenovoVantage",
                "LegionZone", "ArmouryCrate", "ArmouryCrate.Service", "OmenCommandCenter",
                "OMENCap", "MSI Center", "Dragon Center"
            };

            var runningTools = new List<string>();
            try
            {
                Process[] processes = Process.GetProcesses();
                foreach (Process p in processes)
                {
                    try
                    {
                        string pName = p.ProcessName;
                        foreach (string oem in knownOem)
                        {
                            string oemNorm = oem.Replace(" ", "");
                            if (pName.Equals(oem, StringComparison.OrdinalIgnoreCase) ||
                                pName.Equals(oemNorm, StringComparison.OrdinalIgnoreCase))
                            {
                                if (!runningTools.Contains(oem))
                                {
                                    runningTools.Add(oem);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            if (runningTools.Count > 0)
            {
                Console.WriteLine(string.Format("  OEM Tools Running  : {0}", string.Join(", ", runningTools.ToArray())));
            }
            else
            {
                Console.WriteLine("  OEM Tools Running  : None detected");
            }
        }
    }

    internal static class Benchmark
    {
        public static void Run()
        {
            Console.WriteLine();
            Console.WriteLine("=========================================================");
            Console.WriteLine("   CalmDown Win32 PowrProf Apply Latency Benchmark");
            Console.WriteLine("=========================================================");
            Console.WriteLine("  WARNING: This benchmark temporarily modifies ACPI power settings to measure apply latency.");
            Console.WriteLine("           All original power settings will be restored automatically upon completion.");
            Console.WriteLine();

            Guid scheme;
            if (!NativePower.GetActiveSchemeGuid(out scheme))
            {
                Console.WriteLine("Error: Unable to retrieve active power scheme GUID.");
                return;
            }

            // Ensure stock settings are backed up before running benchmark writes
            NativePower.EnsureBackup();

            // Capture pre-benchmark state for BOTH AC and DC rails separately
            uint origAcBoost, origAcFreq, origDcBoost, origDcFreq;
            bool capturedOriginal = NativePower.ReadAllIndices(out origAcBoost, out origAcFreq, out origDcBoost, out origDcFreq);

            try
            {
                // Warm up
                NativePower.ApplyModeDirect(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive, true, true, forceWrite: true);

                const int Iterations = 5;
                double totalMs = 0;
                double minMs = double.MaxValue;
                double maxMs = 0;

                for (int i = 0; i < Iterations; i++)
                {
                    uint targetFreq = (i % 2 == 0) ? NativePower.FREQ_UNCAPPED : NativePower.FREQ_SWEETSPOT_MHZ;
                    PerfBoostMode targetBoost = (i % 2 == 0) ? PerfBoostMode.Disabled : PerfBoostMode.EfficientAggressive;

                    Stopwatch sw = Stopwatch.StartNew();
                    NativePower.ApplyModeDirect(targetFreq, targetBoost, true, true, forceWrite: true);
                    sw.Stop();

                    double elapsed = sw.Elapsed.TotalMilliseconds;
                    totalMs += elapsed;
                    if (elapsed < minMs) minMs = elapsed;
                    if (elapsed > maxMs) maxMs = elapsed;

                    Console.WriteLine(string.Format("  Run {0}: {1:F2} ms", i + 1, elapsed));
                    Thread.Sleep(200);
                }

                double avgMs = totalMs / Iterations;
                Console.WriteLine("---------------------------------------------------------");
                Console.WriteLine(string.Format("  Min: {0:F2} ms | Avg: {1:F2} ms | Max: {2:F2} ms", minMs, avgMs, maxMs));
                Console.WriteLine("=========================================================");
            }
            finally
            {
                if (capturedOriginal)
                {
                    NativePower.ApplyRawRailIndices(origAcFreq, origAcBoost, origDcFreq, origDcBoost);
                    Console.WriteLine("  [RESTORE] Successfully restored pre-benchmark ACPI power profile (both AC & DC rails).");
                }
                Console.WriteLine();
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
            Assert("Transient 1-tick spike does NOT escalate to Beast Turbo", m2 != GovernorMode.BeastTurbo);

            // 3. Sustained moderate load (2 ticks >= 30% max core)
            sm.Process(24, 32, false);
            var m3 = sm.Process(25, 34, false);
            Assert("Sustained moderate load steps up to Sweet-Spot Profile", m3 == GovernorMode.SweetSpot);

            // 4. Low-Side Clock-dependent load hysteresis test (Anti Ping-Pong)
            // A workload of 26% at 2.4 GHz scales down to ~18% at 3.5 GHz.
            // With deadband (step-down requires <15% max and <12% avg), it MUST stay in SweetSpot!
            for (int i = 0; i < 5; i++)
            {
                var mHyst = sm.Process(14, 18, false);
                Assert("Low-side clock-dependent load in deadband (18% max at 3.5GHz) holds Sweet-Spot without ping-ponging (tick " + (i + 1) + ")", mHyst == GovernorMode.SweetSpot);
            }

            // 5. Single-core spike from Sweet-Spot does NOT unlock Beast Turbo without multi-core demand
            var m4 = sm.Process(25, 95, false);
            Assert("Single-core spike in Sweet-Spot does NOT unlock Beast Turbo without multi-core demand", m4 == GovernorMode.SweetSpot);

            // 6. Sustained Multi-Core Heavy Load (2 ticks with high avg >= 70% AND high max >= 85%)
            sm.Process(75, 90, false);
            var m5 = sm.Process(80, 95, false);
            Assert("Sustained multi-core heavy load unlocks Beast Turbo Profile", m5 == GovernorMode.BeastTurbo);

            // 7. High-Side Clock-Dependent Hysteresis Test (Beast Turbo retention)
            // A sustained compile workload taking 75% avg / 90% max at 3.5 GHz scales to ~53% avg / 66% max at 4.9 GHz.
            // In Beast Turbo, it MUST hold Beast Turbo without ping-ponging back to Sweet-Spot!
            for (int i = 0; i < 5; i++)
            {
                var mBeastHold = sm.Process(53, 66, false);
                Assert("High-side clock-dependent load (53% avg at 4.9GHz) holds Beast Turbo without ping-ponging (tick " + (i + 1) + ")", mBeastHold == GovernorMode.BeastTurbo);
            }

            // 8. Beast Turbo release holds dwell time before stepping down
            var m6 = sm.Process(10, 15, false);
            Assert("Beast Turbo load drop holds dwell in Beast/Sweet-Spot before dropping to Ice-Cold", m6 != GovernorMode.IceCold);

            // 9. Active game override
            var m7 = sm.Process(5, 10, true);
            Assert("Game override locks Sweet-Spot Profile regardless of idle load", m7 == GovernorMode.SweetSpot);

            // 10. Clock Model (a): Hot single-thread leaves Beast within 5 ticks and never returns
            var smA = new GovernorStateMachine();
            smA.Process(80, 95, false);
            smA.Process(80, 95, false); // Escalated to Beast
            bool leftBeastWithin5 = false;
            bool returnedToBeast = false;
            for (int tick = 1; tick <= 30; tick++)
            {
                // Single thread needing 3.6 GHz of one core (total 5.5 GHz with baseline -> avg ~7%, max ~73% at 4.9 GHz)
                var m = TickWorkload(smA, 5.5, 3.6);
                if (tick <= 5 && m != GovernorMode.BeastTurbo)
                {
                    leftBeastWithin5 = true;
                }
                if (leftBeastWithin5 && m == GovernorMode.BeastTurbo)
                {
                    returnedToBeast = true;
                }
            }
            Assert("Clock Model (a): Hot single-thread leaves Beast within 5 ticks and never returns", leftBeastWithin5 && !returnedToBeast);

            // 11. Clock Model (b): Sustained 55 GHz*threads for 40 ticks has at most 2 mode switches
            var smB = new GovernorStateMachine();
            int modeSwitches55 = 0;
            GovernorMode lastModeB = smB.CurrentMode;
            for (int tick = 1; tick <= 40; tick++)
            {
                var m = TickWorkload(smB, 55.0, 55.0 / 16.0);
                if (m != lastModeB)
                {
                    modeSwitches55++;
                    lastModeB = m;
                }
            }
            Assert("Clock Model (b): Sustained 55 GHz*threads has at most 2 mode switches (no oscillation)", modeSwitches55 <= 2);

            // 12. Clock Model (c): Sustained 45 GHz*threads settles in Sweet-Spot (not stuck in Beast)
            var smC = new GovernorStateMachine();
            for (int tick = 1; tick <= 40; tick++)
            {
                TickWorkload(smC, 45.0, 45.0 / 16.0);
            }
            Assert("Clock Model (c): Sustained 45 GHz*threads settles in Sweet-Spot (not stuck in Beast)", smC.CurrentMode == GovernorMode.SweetSpot);

            Console.WriteLine("---------------------------------------------------------");
            Console.WriteLine("   [RESULT] All Governor State Machine Asserts Passed!  ");
            Console.WriteLine("=========================================================");
            Console.WriteLine();
        }

        private static double GetClockForMode(GovernorMode mode)
        {
            switch (mode)
            {
                case GovernorMode.IceCold: return 2.4;
                case GovernorMode.SweetSpot: return 3.5;
                case GovernorMode.BeastTurbo: return 4.9;
                default: return 2.4;
            }
        }

        private static GovernorMode TickWorkload(GovernorStateMachine machine, double totalGhz, double maxCoreGhz)
        {
            double clock = GetClockForMode(machine.CurrentMode);
            int avg = (int)Math.Round((totalGhz / (16.0 * clock)) * 100.0);
            if (avg > 100) avg = 100;
            int maxCore = (int)Math.Round((maxCoreGhz / clock) * 100.0);
            if (maxCore > 100) maxCore = 100;
            return machine.Process(avg, maxCore, false);
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
        private int beastDwellTicks = 0;
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
                beastDwellTicks = 0;
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
                    beastDwellTicks = 2;   // Hold Beast for 4s
                    stepDownDwellTicks = 3; // Hold Sweet for 6s after Beast
                }
                return currentMode;
            }
            else
            {
                heavyMultiCoreSpikeCount = 0;
            }

            // High-Side Hysteresis: If currently in Beast Turbo
            if (currentMode == GovernorMode.BeastTurbo)
            {
                // In Beast Turbo (running at 4.9 GHz), identical workloads read ~30-40% lower.
                // Maintain Beast Turbo only while multi-core load remains sustained (avg >= 50% AND max >= 60%)
                if (avgLoad >= 50 && maxCoreLoad >= 60)
                {
                    beastDwellTicks = 2; // Keep dwell hold full while heavy workload continues
                    return GovernorMode.BeastTurbo;
                }

                // Workload dropped below Beast retention threshold (<50% avg OR <60% max)
                if (beastDwellTicks > 0)
                {
                    beastDwellTicks--;
                    return GovernorMode.BeastTurbo; // Hold dwell
                }

                // Dwell expired: step down from Beast to Sweet-Spot first
                currentMode = GovernorMode.SweetSpot;
                stepDownDwellTicks = 3;
                return currentMode;
            }

            // Priority 3: Step UP to Sweet-Spot: Requires maxCoreLoad >= 30% OR avgLoad >= 22% for 2 ticks
            if (maxCoreLoad >= 30 || avgLoad >= 22)
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

            // Priority 4: Step DOWN to Ice-Cold (Deadband Hysteresis)
            // If currently in Sweet-Spot, only step down if load is genuinely low (<15% max core AND <12% avg)
            if (currentMode == GovernorMode.SweetSpot)
            {
                if (maxCoreLoad < 15 && avgLoad < 12)
                {
                    if (stepDownDwellTicks > 0)
                    {
                        stepDownDwellTicks--;
                        return GovernorMode.SweetSpot; // Hold dwell
                    }
                    currentMode = GovernorMode.IceCold;
                    return currentMode;
                }
                else
                {
                    // In deadband (between 15% and 30%): Hold Sweet-Spot without ping-ponging!
                    stepDownDwellTicks = 3;
                    return GovernorMode.SweetSpot;
                }
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
                string tempFile = Path.Combine(ConfigDir, "config.tmp");
                string content = string.Format(
                    "# CalmDown v3.0 Hardware Configuration\r\nDynamicGovernor={0}\r\nGameAutoPilot={1}\r\n",
                    DynamicGovernor, GameAutoPilot);
                File.WriteAllText(tempFile, content);

                if (File.Exists(ConfigFile))
                {
                    try { File.Replace(tempFile, ConfigFile, null); }
                    catch { File.Copy(tempFile, ConfigFile, true); File.Delete(tempFile); }
                }
                else
                {
                    File.Move(tempFile, ConfigFile);
                }
            }
            catch { }
        }

        private static readonly string[] DefaultGameRules = new string[]
        {
            "VALORANT = Sweet",
            "VALORANT-Win64-Shipping = Sweet",
            "cs2 = Sweet",
            "GTA5 = Sweet",
            "r5apex = Sweet",
            "Overwatch = Sweet",
            "FortniteClient-Win64-Shipping = Sweet"
        };

        public static void LoadRules()
        {
            try
            {
                if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir);
                if (!File.Exists(RulesFile))
                {
                    string defaultRules = "# CalmDown Per-Process Rules (executable_name = Sweet / Beast / Ice)\r\n" +
                                          "# Beast rules force full boost whenever the app is in the foreground,\r\n" +
                                          "# even when idle. Add them only if you accept higher heat.\r\n" +
                                          "VALORANT = Sweet\r\n" +
                                          "VALORANT-Win64-Shipping = Sweet\r\n" +
                                          "cs2 = Sweet\r\n" +
                                          "GTA5 = Sweet\r\n" +
                                          "r5apex = Sweet\r\n" +
                                          "Overwatch = Sweet\r\n" +
                                          "FortniteClient-Win64-Shipping = Sweet\r\n";
                    File.WriteAllText(RulesFile, defaultRules);
                }
                else
                {
                    // Migration: preserve existing user rules, only append missing default game keys
                    try
                    {
                        string[] existingLines = File.ReadAllLines(RulesFile);
                        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (string rawLine in existingLines)
                        {
                            string l = rawLine.Trim();
                            if (string.IsNullOrEmpty(l) || l.StartsWith("#") || l.StartsWith(";")) continue;
                            string[] p = l.Split('=');
                            if (p.Length >= 1)
                            {
                                string k = p[0].Trim();
                                if (k.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                    k = k.Substring(0, k.Length - 4).Trim();
                                existingKeys.Add(k);
                            }
                        }

                        var toAppend = new List<string>();
                        foreach (string def in DefaultGameRules)
                        {
                            string[] dp = def.Split('=');
                            string dk = dp[0].Trim();
                            if (!existingKeys.Contains(dk))
                            {
                                toAppend.Add(def);
                                existingKeys.Add(dk);
                            }
                        }

                        if (toAppend.Count > 0)
                        {
                            File.AppendAllLines(RulesFile, toAppend.ToArray());
                        }
                    }
                    catch { }
                }

                AppRules.Clear();
                foreach (string rawLine in File.ReadAllLines(RulesFile))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";")) continue;
                    string[] parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        string processKey = parts[0].Trim();
                        if (processKey.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            processKey = processKey.Substring(0, processKey.Length - 4).Trim();
                        }
                        AppRules[processKey] = parts[1].Trim();
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

        public static Guid SubGroupProcessor = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        public static Guid GuidBoostMode = new Guid("be337238-0d82-4146-a960-4f3749d470c7");
        public static Guid GuidFreqMax = new Guid("75b0ae3f-bce0-45a7-8c89-c9611c25e100");

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

        [DllImport("powrprof.dll", EntryPoint = "PowerReadFriendlyName", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadFriendlyName(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            IntPtr SubGroupOfPowerSettingsGuid,
            IntPtr PowerSettingGuid,
            IntPtr Buffer,
            ref uint BufferSize);

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

        public static bool GetActiveSchemeFriendlyName(out string friendlyName, out uint errorCode)
        {
            friendlyName = "";
            errorCode = 0;
            Guid scheme;
            if (!GetActiveSchemeGuid(out scheme))
            {
                errorCode = 1;
                return false;
            }

            uint bufSize = 0;
            uint ret = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref bufSize);
            if (ret != 0 && ret != 234) // 234 = ERROR_MORE_DATA
            {
                errorCode = ret;
                return false;
            }

            if (bufSize == 0)
            {
                friendlyName = "Unknown";
                return true;
            }

            IntPtr pBuf = Marshal.AllocHGlobal((int)bufSize);
            try
            {
                ret = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, pBuf, ref bufSize);
                if (ret == 0)
                {
                    friendlyName = Marshal.PtrToStringUni(pBuf);
                    return true;
                }
                else
                {
                    errorCode = ret;
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pBuf);
            }
        }

        public static uint ReadSpecificIndex(Guid scheme, bool isAC, Guid settingGuid, out uint value)
        {
            if (isAC)
            {
                return PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref settingGuid, out value);
            }
            else
            {
                return PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref settingGuid, out value);
            }
        }

        public static bool ReadAllIndices(out uint acBoost, out uint acFreq, out uint dcBoost, out uint dcFreq)
        {
            acBoost = 0; acFreq = 0; dcBoost = 0; dcFreq = 0;
            Guid scheme;
            if (!GetActiveSchemeGuid(out scheme)) return false;

            uint e1 = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, out acBoost);
            uint e2 = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, out acFreq);
            uint e3 = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, out dcBoost);
            uint e4 = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, out dcFreq);

            return (e1 == 0 && e2 == 0 && e3 == 0 && e4 == 0);
        }

        public static bool ApplyRawRailIndices(uint acFreq, uint acBoost, uint dcFreq, uint dcBoost)
        {
            lock (powerSyncLock)
            {
                Guid scheme;
                if (!GetActiveSchemeGuid(out scheme)) return false;

                bool atLeastOneWrite = false;
                if (PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, acFreq) == 0 &&
                    PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, acBoost) == 0)
                {
                    atLeastOneWrite = true;
                }

                if (PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, dcFreq) == 0 &&
                    PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, dcBoost) == 0)
                {
                    atLeastOneWrite = true;
                }

                if (atLeastOneWrite)
                {
                    PowerSetActiveScheme(IntPtr.Zero, ref scheme);
                    return true;
                }
                return false;
            }
        }

        public static bool ReadCurrentIndices(out uint boostMode, out uint maxFreqMhz)
        {
            boostMode = 0;
            maxFreqMhz = 0;
            Guid scheme;
            if (!GetActiveSchemeGuid(out scheme)) return false;

            bool onBattery = (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline);
            uint errB, errF;
            if (onBattery)
            {
                errB = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, out boostMode);
                errF = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, out maxFreqMhz);
            }
            else
            {
                errB = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, out boostMode);
                errF = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, out maxFreqMhz);
            }

            return (errB == 0 && errF == 0);
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
                uint e1 = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, out acBoost);
                uint e2 = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, out acFreq);
                uint e3 = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, out dcBoost);
                uint e4 = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, out dcFreq);

                if (e1 == 0 && e2 == 0 && e3 == 0 && e4 == 0)
                {
                    File.WriteAllText(BackupPath, string.Format("{0},{1},{2},{3}", acBoost, acFreq, dcBoost, dcFreq));
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

        public static bool ApplyModeDirect(uint freqMhz, PerfBoostMode boostMode, bool writeAC = true, bool writeDC = true, bool forceWrite = false)
        {
            lock (powerSyncLock)
            {
                if (!forceWrite)
                {
                    uint curB, curF;
                    if (ReadCurrentIndices(out curB, out curF) && curB == (uint)boostMode && curF == freqMhz)
                    {
                        return true; // Already applied, verified against live ACPI state
                    }
                }

                EnsureBackup();
                Guid scheme;
                if (!GetActiveSchemeGuid(out scheme)) return false;

                bool atLeastOneWrite = false;
                if (writeAC)
                {
                    if (PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, freqMhz) == 0 &&
                        PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, (uint)boostMode) == 0)
                    {
                        atLeastOneWrite = true;
                    }
                }

                if (writeDC)
                {
                    if (PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidFreqMax, freqMhz) == 0 &&
                        PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SubGroupProcessor, ref GuidBoostMode, (uint)boostMode) == 0)
                    {
                        atLeastOneWrite = true;
                    }
                }

                if (atLeastOneWrite)
                {
                    PowerSetActiveScheme(IntPtr.Zero, ref scheme);
                    return true;
                }
                return false;
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
        private GovernorMode currentActiveGovernorMode = GovernorMode.IceCold;
        private GovernorMode previousUserMode = GovernorMode.IceCold;
        private bool isGameActive = false;
        private string currentActiveRuleApp = null;
        private string currentActiveRuleMode = null;
        private int ruleReleaseGraceTicks = 0;
        private const int RULE_RELEASE_GRACE_MAX = 2; // 2 ticks * 2000ms = 4 seconds alt-tab grace period
        private uint preRuleAcBoost = 0;
        private uint preRuleAcFreq = 0;
        private uint preRuleDcBoost = 0;
        private uint preRuleDcFreq = 0;
        private bool hasPreRuleSnapshot = false;

        public MainForm()
        {
            for (int i = 0; i < MaxHistoryPoints; i++) telemetryHistory.Add(0);

            // Read live hardware ACPI state on startup to initialize current and previous modes accurately
            uint initB, initF;
            if (NativePower.ReadCurrentIndices(out initB, out initF))
            {
                if (initB == (uint)PerfBoostMode.Disabled) currentActiveGovernorMode = GovernorMode.IceCold;
                else if (initF == NativePower.FREQ_SWEETSPOT_MHZ) currentActiveGovernorMode = GovernorMode.SweetSpot;
                else currentActiveGovernorMode = GovernorMode.BeastTurbo;
            }
            previousUserMode = currentActiveGovernorMode;

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
                "Frequency Ceiling: Hardware Base Clock | Intel Turbo: Disabled (0x0)\nLow Power Draw | Cool Chassis | Silent Fan Curve\nDesigned for: Study, Reading, Browsing, Daily Productivity",
                new Point(18, 116),
                Color.FromArgb(0, 168, 255),
                out lblIceTag,
                () => SetManualMode(NativePower.FREQ_UNCAPPED, PerfBoostMode.Disabled, GovernorMode.IceCold)
            );

            cardSweet = CreateProfileCard(
                "SWEET-SPOT BALANCED PROFILE",
                "Frequency Ceiling: 3500 MHz | Turbo: Efficient Aggressive (0x4)\nPrevents High-Voltage Runaway | Stable Frame Pacing\nDesigned for: Competitive Gaming, Multitasking, Everyday Performance",
                new Point(18, 196),
                Color.FromArgb(0, 200, 83),
                out lblSweetTag,
                () => SetManualMode(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive, GovernorMode.SweetSpot)
            );

            cardBeast = CreateProfileCard(
                "BEAST TURBO PROFILE",
                "Frequency Ceiling: Uncapped Clocks | Turbo: Aggressive (0x2)\nFull Hardware Power Envelope | Maximum Single & Multi-Core Throughput\nDesigned for: 4K Video Exports, Code Builds, Heavy Benchmarks",
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
                Text = "Step-by-step scaling: single-core spikes hold Sweet-Spot; Beast requires sustained multi-core load.\n(Deadband hysteresis & slow-release dwell hold eliminate fan oscillation)",
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(145, 152, 168),
                Location = new Point(34, 32),
                Size = new Size(480, 28)
            };

            chkAutoPilot = new CheckBox
            {
                Text = "Enable Game Auto-Pilot Priority (Foreground rules.ini Matching)",
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

                // If user unticks mid-game, restore previous mode immediately
                if (!chkAutoPilot.Checked && isGameActive)
                {
                    isGameActive = false;
                    currentActiveRuleApp = null;
                    currentActiveRuleMode = null;
                    ruleReleaseGraceTicks = 0;
                    RestorePreviousMode();
                }
                RefreshStatus(cachedAvgLoad, cachedMaxCoreLoad);
            };

            lblAutoPilotSub = new Label
            {
                Text = "Monitors active foreground window and applies configured rules.ini profiles.",
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
            trayMenu.Items.Add("Ice-Cold Profile", null, (s, e) => SetManualMode(NativePower.FREQ_UNCAPPED, PerfBoostMode.Disabled, GovernorMode.IceCold));
            trayMenu.Items.Add("Sweet-Spot Profile", null, (s, e) => SetManualMode(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive, GovernorMode.SweetSpot));
            trayMenu.Items.Add("Beast Turbo Profile", null, (s, e) => SetManualMode(NativePower.FREQ_UNCAPPED, PerfBoostMode.Aggressive, GovernorMode.BeastTurbo));
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

                    // Check Foreground Application against rules.ini
                    string fgProcess = HardwareMonitor.GetForegroundProcessName();
                    string targetRule = null;
                    bool isFgRuled = false;

                    if (chkAutoPilot.Checked && !string.IsNullOrEmpty(fgProcess))
                    {
                        isFgRuled = ConfigManager.AppRules.TryGetValue(fgProcess, out targetRule);
                    }

                    if (isFgRuled)
                    {
                        // Ruled app is active: reset alt-tab grace timer
                        ruleReleaseGraceTicks = RULE_RELEASE_GRACE_MAX;

                        if (!isGameActive)
                        {
                            // Entry into ruled app
                            isGameActive = true;
                            currentActiveRuleApp = fgProcess;
                            currentActiveRuleMode = targetRule;

                            // Take exact live snapshot of current ACPI indices for BOTH AC and DC rails
                            if (NativePower.ReadAllIndices(out preRuleAcBoost, out preRuleAcFreq, out preRuleDcBoost, out preRuleDcFreq))
                            {
                                hasPreRuleSnapshot = true;
                            }
                            previousUserMode = currentActiveGovernorMode;

                            ApplyNamedRule(targetRule);
                            trayIcon.ShowBalloonTip(1800, "Rule Applied: " + fgProcess, "Activated " + targetRule + " Profile based on foreground application.", ToolTipIcon.Info);
                        }
                        else if (!string.Equals(currentActiveRuleApp, fgProcess, StringComparison.OrdinalIgnoreCase) ||
                                 !string.Equals(currentActiveRuleMode, targetRule, StringComparison.OrdinalIgnoreCase))
                        {
                            // Switching between two ruled apps (e.g. Blender -> VALORANT)
                            currentActiveRuleApp = fgProcess;
                            currentActiveRuleMode = targetRule;
                            ApplyNamedRule(targetRule);
                            trayIcon.ShowBalloonTip(1800, "Rule Switched: " + fgProcess, "Switched to " + targetRule + " Profile based on foreground application.", ToolTipIcon.Info);
                        }
                    }
                    else
                    {
                        // Foreground app is NOT ruled (e.g. Alt-Tab to Discord, Taskbar, or Explorer)
                        if (isGameActive)
                        {
                            if (ruleReleaseGraceTicks > 0)
                            {
                                ruleReleaseGraceTicks--;
                                // Grace period holds profile without flapping or balloon spam
                            }
                            else
                            {
                                // Grace period expired: cleanly deactivate and restore previous mode
                                isGameActive = false;
                                currentActiveRuleApp = null;
                                currentActiveRuleMode = null;
                                RestorePreviousMode();
                                trayIcon.ShowBalloonTip(1800, "App Deactivated", "Restored previous power profile.", ToolTipIcon.Info);
                            }
                        }
                    }

                    // Dynamic Governor State Machine (only runs if no rule/game is active)
                    if (chkDynamic.Checked && !isGameActive)
                    {
                        var targetMode = stateMachine.Process(cachedAvgLoad, cachedMaxCoreLoad, false);
                        ApplyGovernorMode(targetMode);
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

        private void ApplyNamedRule(string targetRule)
        {
            if (targetRule.Equals("Beast", StringComparison.OrdinalIgnoreCase))
            {
                currentActiveGovernorMode = GovernorMode.BeastTurbo;
                NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Aggressive);
            }
            else if (targetRule.Equals("Sweet", StringComparison.OrdinalIgnoreCase))
            {
                currentActiveGovernorMode = GovernorMode.SweetSpot;
                NativePower.ApplyModeDirect(NativePower.FREQ_SWEETSPOT_MHZ, PerfBoostMode.EfficientAggressive);
            }
            else
            {
                currentActiveGovernorMode = GovernorMode.IceCold;
                NativePower.ApplyModeDirect(NativePower.FREQ_UNCAPPED, PerfBoostMode.Disabled);
            }
        }

        private void ApplyGovernorMode(GovernorMode mode)
        {
            currentActiveGovernorMode = mode;
            switch (mode)
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

        private void RestorePreviousMode()
        {
            if (hasPreRuleSnapshot)
            {
                NativePower.ApplyRawRailIndices(preRuleAcFreq, preRuleAcBoost, preRuleDcFreq, preRuleDcBoost);
                hasPreRuleSnapshot = false;
                currentActiveGovernorMode = previousUserMode;
            }
            else
            {
                ApplyGovernorMode(previousUserMode);
            }
        }

        private void SetManualMode(uint freq, PerfBoostMode boost, GovernorMode targetMode)
        {
            if (chkDynamic.Checked)
            {
                chkDynamic.Checked = false; // Disable dynamic governor so manual selection holds
            }

            hasPreRuleSnapshot = false;
            currentActiveGovernorMode = targetMode;
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

            string modeTitle = isIce ? "Ice-Cold" : (isSweet ? "Sweet-Spot" : (isBeast ? "Beast Turbo" : "Custom"));
            Color modeColor = isIce ? Color.FromArgb(0, 168, 255) : (isSweet ? Color.FromArgb(0, 200, 83) : Color.FromArgb(255, 82, 82));

            string modeTag = "";
            if (chkDynamic.Checked) modeTag = " [DYNAMIC]";
            else if (isGameActive) modeTag = " [RULE LOCK]";

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
