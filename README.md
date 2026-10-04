# 🧘 CalmDown v3.0

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6.svg)](https://microsoft.com/windows)
[![Binary](https://img.shields.io/badge/CalmDown.exe-25%20KB%20(Native%20Win32)-brightgreen.svg)]()
[![Tested Hardware](https://img.shields.io/badge/Tested%20On-Acer%20ALG%20(i7--13620H)-orange.svg)]()
[![Architecture](https://img.shields.io/badge/Architecture-Driver--Free%20User--Space-blueviolet.svg)]()

> **Autonomous, Driver-Free CPU Thermal & Power Governor for Windows Gaming Laptops.**  
> Native 0-dependency Windows utility that manages ACPI frequency ceilings to eliminate aggressive turbo voltage spikes.  
> Verified telemetry drop: **9°C peak / ~5°C average reduction in Valorant**, and **~60°C–65°C during everyday study/browsing**.

---

## 🚀 What's New in v3.0 (Major Architectural Upgrade)

- ⚡ **Direct Win32 `powrprof.dll` Engine:** Replaced `powercfg.exe` command-line process spawning with direct Win32 P/Invoke (`PowerWriteACValueIndex`, `PowerSetActiveScheme`). Measured apply latency dropped from **~450 ms down to ~51 ms** with zero child-process overhead.
- 🎯 **Anti-Flap Max-Core Aware Governor:** Tracks per-core busy times via `NtQuerySystemInformation(8)`. Implements a step-by-step state machine:
  - Transient single-core spikes (e.g. Defender scan or browser tab) hold **Sweet-Spot (3500 MHz)**.
  - **Beast Turbo (Uncapped 4.9 GHz)** is only unlocked on **sustained multi-core workloads** (avg > 70% & max > 85% for 4+ seconds).
  - Includes a 6-second slow-release dwell timer to eliminate rapid fan/power oscillations.
- 🎮 **Per-Process Rules & Game Detection:** Reads `%LOCALAPPDATA%\CalmDown\rules.ini` to map foreground applications directly to profiles (`VALORANT.exe = Sweet`, `Premiere.exe = Beast`, etc.).
- 🧪 **Built-in State Machine Verification (`--selftest`):** Includes a CLI self-test suite (`CalmDown.exe --selftest`) that feeds synthetic load traces into the governor and asserts state transitions.
- 📈 **Live GDI Hardware Sparkline:** Integrated a smooth rolling 50-point telemetry graph directly into the Dark UI, visualizing real-time CPU load spikes.
- 🛡️ **Driver-Free User-Space Design:** Operates entirely within standard user-space via documented Windows ACPI power APIs, avoiding third-party ring-0 kernel drivers.
- 💾 **State Persistence & Mutex Recovery:** Config settings are saved to `%LOCALAPPDATA%\CalmDown\config.ini`. Single-instance mutex handles abandoned instances and restores existing windows via Win32 IPC messaging.

---

## 📌 The Problem: Why Many Budget Gaming Laptops Overheat

Modern high-performance processors (like the **Intel Core i7-13620H, i5-13420H, i5-12450H**) are capable of massive clock speeds, but:

1. **Aggressive Boost Curve:** Out of the box, Windows power schemes set CPU boost to *Aggressive*. Even on moderate tasks or locked-framerate gaming, the CPU attempts to boost toward 4.9 GHz, drawing high wattage and voltage into compact laptop chassis.
2. **Shared Cooling Pipes:** In many budget and mid-range gaming designs, the CPU and GPU share the same copper heatpipes. When the CPU runs hot unnecessarily, heat transfers across to the GPU.
3. **Locked Undervolting:** On 12th/13th/14th Gen Intel H-series chips, undervolting is hardware-locked by firmware, so traditional offset tools cannot offset voltages.
4. **The Voltage Physics:** Power grows far faster than linearly with frequency because voltage must rise with higher clocks ($P \propto V^2 \times f$). In locked-framerate gaming (e.g. 114 FPS in Valorant), pumping 70W+ into the CPU yields zero extra frames and creates 40W+ of pure wasted heat.

---

## 💡 The Solution: Frequency Capping & Boost Governor Presets

**CalmDown** is a **standalone native Windows executable (`CalmDown.exe`)** that interfaces directly with the **Windows Power Subsystem (`powrprof.dll`)**. Instead of trying to undervolt or modify hardware, it manages the CPU frequency ceiling and boost governor to curb excessive voltage spikes.

---

## 🎮 The 3 Operating Presets

```
+---------------------------------------------------------------------------------+
|                                 CalmDown v3.0                                   |
+---------------------------------------------------------------------------------+
|  [1] ❄️ ICE-COLD PROFILE    [Base Clock ~2.4 GHz | ~60°C - 65°C (Idle/Study)]   |
|      Silent Fans - Zero Keyboard Heat - Maximum Battery Efficiency              |
|      👉 Best for: Normal Study, Web Browsing, YouTube, Daily Tasks              |
+---------------------------------------------------------------------------------+
|  [2] ⚖️ SWEET-SPOT BALANCED  [Capped at 3500 MHz | ~72°C - 78°C]                 |
|      High Clock Headroom - Prevents Severe Spikes - Controlled Heat             |
|      👉 Best for: Valorant, CS2, Competitive Gaming, Multitasking               |
+---------------------------------------------------------------------------------+
|  [3] 🔥 BEAST TURBO          [Uncapped 4.9 GHz Boost | Full Power]              |
|      Standard Windows Turbo Profile - Maximum Power & Throughput                |
|      👉 Best for: 4K Video Exports, Code Compilation, Heavy Benchmarks          |
+---------------------------------------------------------------------------------+
```

---

## 📊 Verified Real-World Benchmark (Tested in Valorant)

*Tested on: Acer ALG AL15G (Intel Core i7-13620H, NVIDIA RTX 3050 6GB, 16GB RAM, FPS capped at 114).*

| Metric | Stock Windows Boost (Powerful Mode) | CalmDown (Ice-Cold Mode) | Verified Difference |
| :--- | :---: | :---: | :---: |
| **Peak (Max) CPU Temp** | **92°C** (Near Thermal Throttling) | **83°C** | **9°C Cooler Peak** |
| **Average CPU Temp** | **81.7°C** | **76.8°C** | **4.9°C Lower Average** |
| **CPU Clock Behavior** | Frequent 4.5+ GHz spikes | **Steady 2.4 GHz base** | Spikes smoothed out |
| **Valorant FPS** | 114 FPS (Locked) | **114 FPS (Locked)** | **Stable, zero frame drops** |
| **Average GPU Temp** | 70.8°C | **69.7°C** | Remained cool |
| **Average GPU Power** | 31.8W | **32.5W** | Identical power draw |

> *Note: In non-gaming everyday workloads (web browsing, studying, YouTube), CPU temperatures sit comfortably at **~60°C–65°C** with near-silent fans.*

---

## 🥊 CalmDown vs Traditional Tools

| Feature | OEM Control Center (Acer / ASUS) | ThrottleStop / XTU | CalmDown.exe v3.0 |
| :--- | :--- | :--- | :--- |
| **Architecture** | Heavy Electron / WPF background suite | Ring-0 kernel-mode driver | **User-space Win32 (`powrprof.dll`)** |
| **Driver Dependency** | Proprietary services | Custom kernel driver | **0 external drivers (Native Windows APIs)** |
| **Crash Risk** | Low (bloated) | BSOD risk from unstable offsets | **Zero BSOD risk (ACPI managed)** |
| **Footprint** | 150MB+ RAM | 10MB - 30MB RAM | **~25 KB executable**, < 0.1% idle CPU |
| **Autonomous** | Manual profiles | Fixed thresholds | **Dynamic Max-Core Governor + rules.ini** |

---

## 🖥️ Command-Line Interface (CLI)

CalmDown can be called directly from shortcuts, terminal scripts, or custom game launchers:

```cmd
CalmDown.exe --selftest  # Run Governor State Machine test suite
CalmDown.exe --ice       # Activate Ice-Cold Profile (Locked base frequency)
CalmDown.exe --sweet     # Activate Sweet-Spot Profile (3500 MHz cap)
CalmDown.exe --beast     # Activate Beast Turbo Profile (Uncapped boost)
CalmDown.exe --restore   # Reset to original factory power configuration
```

---

## 🔒 Security & Verification

To verify the integrity of the standalone binary:

- **File:** `CalmDown.exe`
- **SHA-256 Checksum:**  
  `0B5D189A18DE7BA07587E46E6E524F8B04C3EC3B9349FAB85FA443A2BD169381`

You can verify the checksum in PowerShell:
```powershell
Get-FileHash CalmDown.exe -Algorithm SHA256
```

---

## 🛠️ Building from Source

CalmDown is written in clean, standard C# and compiles natively using the built-in Windows .NET compiler without installing Visual Studio:

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /platform:x64 /out:"CalmDown.exe" /r:System.Windows.Forms.dll,System.Drawing.dll,System.dll "src\Program.cs"
```

---

## 📜 License

Released under the [MIT License](LICENSE).

**Created by [Anurag Kumar](https://github.com/kumaranurag7278-lgtm)**
