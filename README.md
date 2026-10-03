# 🧘 CalmDown v2.0

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6.svg)](https://microsoft.com/windows)
[![Binary](https://img.shields.io/badge/CalmDown.exe-21%20KB%20(Native%20Win32)-brightgreen.svg)]()
[![Tested Hardware](https://img.shields.io/badge/Tested%20On-Acer%20ALG%20(i7--13620H)-orange.svg)]()

> **Smart CPU Thermal Governor & Auto-Pilot for Gaming Laptops.**  
> Native 0-dependency Windows utility that caps aggressive turbo voltage spikes.  
> Verified telemetry drop: **9°C peak / ~5°C average reduction in Valorant**, and **~60°C–65°C during everyday study/browsing**.

---

## 🚀 What's New in v2.0

- 🎨 **Modern Dark Slate GUI:** Clean, responsive card-based interface with active mode badges and hover feedback.
- 🎮 **Auto-Pilot Game Detection:** Automatically engages **Sweet-Spot Mode (3.5 GHz)** when games (`VALORANT`, `cs2`, `GTA5`, `r5apex`, `FortniteClient`, `Overwatch`) launch, and seamlessly reverts to **Ice-Cold Mode (~2.4 GHz)** when they close.
- 📌 **System Tray Integration:** Minimizes cleanly to the taskbar notification area with right-click quick presets and balloon notifications.
- ⚡ **CLI Automation Flags:** Scriptable via command line (`CalmDown.exe --ice`, `--sweet`, `--beast`, `--restore`) for game launchers or Task Scheduler.
- 🛡️ **Safe Backup & 1-Click Restore:** Automatically backs up stock Windows power scheme indexes on first run and provides a dedicated "Reset to Stock" button.

---

## 📌 The Problem: Why Many Budget Gaming Laptops Overheat

Modern high-performance processors (like the **Intel Core i7-13620H, i5-13420H, i5-12450H**) are capable of massive clock speeds, but:

1. **Aggressive Boost Curve:** Out of the box, Windows power schemes set CPU boost to *Aggressive*. Even on moderate tasks, the CPU attempts to boost toward 4.9 GHz, drawing high wattage and voltage into compact laptop chassis.
2. **Shared Cooling Pipes:** In many budget and mid-range gaming designs, the CPU and GPU share the same copper heatpipes. When the CPU runs hot, heat transfers across to the GPU.
3. **Locked Undervolting:** On 12th/13th/14th Gen Intel H-series chips, undervolting is hardware-protected by firmware, so traditional tools cannot offset voltages.
4. **OEM Software Limits:** Preinstalled Control Centers primarily ramp up fan curves to maximum noise rather than addressing the clock multiplier ceiling.

---

## 💡 The Solution: Frequency Capping & Boost Governor Presets

**CalmDown** is a **standalone native Windows executable (`CalmDown.exe`)** that interfaces directly with the **Windows Power Subsystem (`powercfg`)**. Instead of trying to undervolt or modify hardware, it manages the CPU frequency ceiling and boost governor to curb excessive voltage spikes.

---

## 🎮 The 3 Operating Presets

```
+---------------------------------------------------------------------------------+
|                                 CalmDown v2.0                                   |
+---------------------------------------------------------------------------------+
|  [1] ❄️ ICE-COLD MODE       [Base Clock ~2.4 GHz | ~60°C - 65°C (Idle/Study)]   |
|      Silent Fans - Zero Keyboard Heat - Maximum Battery Efficiency              |
|      👉 Best for: Normal Study, Web Browsing, YouTube, Daily Tasks              |
+---------------------------------------------------------------------------------+
|  [2] ⚖️ SWEET-SPOT BALANCED  [Capped at 3.5 GHz | ~72°C - 78°C]                  |
|      High Clock Headroom - Prevents Severe Spikes - Controlled Heat             |
|      👉 Best for: Valorant, Competitive Gaming, Multitasking                    |
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

## 🥊 CalmDown vs Preinstalled OEM Software

| Feature | OEM Control Center (Acer / Lenovo / ASUS) | CalmDown.exe v2.0 |
| :--- | :--- | :--- |
| **Method** | Cranks fans to high RPM & adjusts broad wattage targets | Directly caps CPU clock frequency ceiling via Windows ACPI |
| **Boost Behavior** | CPU still spikes to maximum boost clocks under burst load | Restricts CPU multiplier ceiling to prevent high voltage spikes |
| **Footprint** | 150MB+ RAM, multiple background services | **Native binary**, tiny memory footprint, 0 external runtimes |
| **Auto-Pilot** | Manual profiles or locked proprietary modes | Automatic background game detection & seamless switching |
| **Workflow** | Often requires navigating nested menus | 1-click toggle, global hotkey (`Ctrl + Alt + C`), tray context menu |

---

## 🖥️ Command-Line Interface (CLI)

CalmDown can be called directly from shortcuts, terminal scripts, or custom game launchers:

```cmd
CalmDown.exe --ice       # Activate Ice-Cold Mode (Locked base frequency)
CalmDown.exe --sweet     # Activate Sweet-Spot Mode (3.5 GHz cap)
CalmDown.exe --beast     # Activate Beast Turbo Mode (Uncapped boost)
CalmDown.exe --restore   # Reset to original stock power configuration
```

---

## 🚀 Quick Start

### Option A: 1-Click Installer (Recommended)
1. Download or clone this repository.
2. Double-click **`Install.bat`**.
3. A desktop shortcut named **CalmDown** is created with hotkey **`Ctrl + Alt + C`**.

### Option B: Standalone Portable Use
- Simply run **`CalmDown.exe`** directly from the root folder. No installation or setup required.

---

## 🔒 Security & Verification

To verify the integrity of the standalone binary:

- **File:** `CalmDown.exe`
- **SHA-256 Checksum:**  
  `E0A49D0CAFC724AD481E940F9217E78FB09BAC2C3D14BFAA4642A79D8B3AA994`

You can verify the checksum in PowerShell:
```powershell
Get-FileHash CalmDown.exe -Algorithm SHA256
```

---

## 🛠️ Building from Source

CalmDown is written in clean C# and compiles natively using the built-in Windows .NET compiler without installing Visual Studio:

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /platform:x64 /out:"CalmDown.exe" /r:System.Windows.Forms.dll,System.Drawing.dll,System.dll "src\Program.cs"
```

---

## 💻 Compatibility & Scope

- **Primary Tested System:** Acer ALG AL15G (Intel Core i7-13620H + RTX 3050).
- **System Requirements:** Windows 10 (20H2+) or Windows 11.
- **Expected Compatibility:** Intel 12th, 13th, and 14th Gen H/HX processors on laptops where Windows ACPI power management controls are supported.
- **Safety:** CalmDown only calls native Windows `powercfg` settings. It does not flash BIOS, modify voltages below hardware specification, or alter physical fan controller firmware.

---

## 📜 License

Released under the [MIT License](LICENSE).

**Created by [Anurag Kumar](https://github.com/kumaranurag7278-lgtm)**
