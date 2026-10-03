# ❄️ CoolBoost Control

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6.svg)](https://microsoft.com/windows)
[![Dependencies](https://img.shields.io/badge/Dependencies-Zero%20(Pure%20Native)-brightgreen.svg)]()
[![Hardware](https://img.shields.io/badge/CPUs-Intel%20Core%20%7C%20AMD%20Ryzen-orange.svg)]()

> **1-Click Thermal & Performance Switcher for Windows Gaming Laptops.**  
> Stop your laptop from hitting 95°C+ on simple tasks and gaming — without sacrificing FPS.

---

## 📌 The Problem: Why Modern Gaming Laptops Overheat

If you own an **Acer ALG / Nitro, Lenovo LOQ / Legion, HP Victus, or ASUS TUF** laptop equipped with modern high-performance processors (like the **Intel Core i7-13620H, i5-13420H, i5-12450H**):

1. **Aggressive Voltage Spikes:** Out of the box, Windows sets Intel Turbo Boost to *Aggressive*. Even when you open a browser tab or move a window, the CPU violently spikes to **4.9 GHz**, pulling **80W–115W** of power.
2. **Shared Copper Heatpipes:** In most budget/mid-range gaming chassis, the CPU and GPU share the same copper cooling pipes. When the CPU runs at 95°C, it literally cooks the GPU alongside it.
3. **Intel Undervolt Protection:** On 12th/13th/14th Gen H-series laptops, Intel has hardware-locked voltage control. Traditional tools like ThrottleStop cannot undervolt these chips.
4. **OEM Software is Useless:** Factory Control Centers (Acer, Lenovo, ASUS) only crank fans to 100% jet-engine noise without addressing the electrical root cause.

---

## 💡 The Solution: Frequency Capping & Boost Governor Control

**CoolBoost Control** bypasses bloated OEM software and directly interfaces with the **Windows Power Subsystem**. By capping the clock ceiling at the exact inflection point before voltage skyrockets exponentially, you get **85%–90% of maximum CPU performance with 50% less heat**.

---

## 🎮 The 3 Operating Modes

```
+---------------------------------------------------------------------------------+
|                               CoolBoost Control                                 |
+---------------------------------------------------------------------------------+
|  [1] ❄️ ICE-COLD MODE       [Base Clock ~2.4 GHz | ~60°C - 65°C]                |
|      Silent Fans - Zero Keyboard Heat - Maximum Battery Life                    |
|      👉 Best for: Normal Study, Web Browsing, YouTube, Daily Tasks              |
+---------------------------------------------------------------------------------+
|  [2] ⚖️ SWEET-SPOT BALANCED  [Capped at 3.5 GHz | ~72°C - 78°C]                  |
|      High FPS - Zero Thermal Throttling - Controlled Thermals                   |
|      👉 Best for: Valorant, CS2, Competitive Gaming, Multitasking               |
+---------------------------------------------------------------------------------+
|  [3] 🔥 BEAST TURBO          [Max Uncapped 4.9 GHz | Full Power]                 |
|      100% Uncapped Speed - Max 115W Boost - Peak Clock Speeds                   |
|      👉 Best for: 4K Video Exports, Code Compilation, Heavy Benchmarks          |
+---------------------------------------------------------------------------------+
```

---

## 📊 Real-World Benchmark (Tested on Acer ALG i7-13620H + RTX 3050)

| Metric | Stock Windows Settings | Sweet-Spot Mode (CoolBoost) | Ice-Cold Mode (CoolBoost) |
| :--- | :---: | :---: | :---: |
| **CPU Clock** | Spiking to 4.9 GHz | **Rock-steady 3.5 GHz** | **Locked 2.4 GHz** |
| **CPU Power Draw** | 85W – 115W | **~40W – 45W** | **25W – 30W** |
| **Valorant Temp** | 95°C (Throttling) | **74°C (Cool & Smooth)** | **66°C (Ice-Cold)** |
| **Valorant FPS** | 200–240 FPS (Stutters) | **220–240 FPS (Stable)** | **180–210 FPS (Stable)** |
| **Keyboard Surface** | Uncomfortably hot | Comfortable | Cool to touch |
| **Fan Noise** | 100% Jet Engine | Moderate / Quiet | Near Silent |

---

## 🚀 Quick Start (Installation in 5 Seconds)

### Step 1: Download
- Click **Code** ➡️ **Download ZIP** on this GitHub page.
- Extract the ZIP anywhere on your PC.

### Step 2: Install
- Double-click **`Install.bat`**.
- It will automatically:
  - Copy the lightweight switcher to `%LOCALAPPDATA%\CoolBoostControl`.
  - Create a **Desktop Shortcut** with a custom thermal icon.
  - Bind the global hotkey: <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>C</kbd>.

### Step 3: Use
- Double-click the desktop shortcut or press <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>C</kbd> anywhere in Windows.
- Click your desired mode, and it applies instantly without restarting!

---

## ❓ Frequently Asked Questions (FAQ)

### Does this disable or harm my GPU?
**No.** Your discrete GPU (NVIDIA RTX 3050, 4060, etc.) remains 100% active at full graphics power. CoolBoost only manages CPU boost behavior. In fact, your GPU will run significantly cooler because the CPU isn't transferring 95°C of heat through the shared cooling pipes.

### Does this void my laptop warranty?
**No.** CoolBoost uses standard, native Windows ACPI power management parameters (`powercfg`). It does not modify BIOS, overclock, or overvolt your hardware.

### How do I uninstall it?
Simply double-click **`Uninstall.bat`**. It instantly restores stock Windows power settings and deletes the shortcut and files.

---

## 💻 Compatibility

- **OS:** Windows 10 (20H2+) / Windows 11 (All versions)
- **CPUs:** Intel Core (10th, 11th, 12th, 13th, 14th Gen, Core Ultra) & AMD Ryzen (4000, 5000, 6000, 7000, 8000 series)
- **Laptops:** Acer (ALG, Nitro, Predator), Lenovo (LOQ, Legion, IdeaPad Gaming), ASUS (TUF, ROG Zephyrus), HP (Victus, Omen), Dell (G15, Alienware), MSI, and Clevo/Tongfang ODMs.

---

## 📜 License

Released under the [MIT License](LICENSE). Free for personal and commercial use.

**Created with ❤️ by [Anurag Kumar](https://github.com/anurag-kumar)**
