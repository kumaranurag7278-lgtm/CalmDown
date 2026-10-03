# CoolBoost Control - Thermal & Power Mode Switcher for Windows Laptops
# Copyright (c) 2026 Anurag Kumar. Released under the MIT License.

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Determine CPU name dynamically
$cpuInfo = Get-CimInstance Win32_Processor | Select-Object -First 1
$cpuName = if ($cpuInfo) { $cpuInfo.Name.Trim() } else { "System Processor" }

# Query current power settings
$boostOut = powercfg /qh SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE | Select-String "Current AC Power Setting Index:"
$boostVal = if ($boostOut) { ($boostOut -split " ")[-1].Trim() } else { "0x00000002" }

$freqOut = powercfg /qh SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX | Select-String "Current AC Power Setting Index:"
$freqVal = if ($freqOut) { ($freqOut -split " ")[-1].Trim() } else { "0x00000000" }

$activeMode = "Beast Turbo"
if ($boostVal -eq "0x00000000") {
    $activeMode = "Ice-Cold"
} elseif ($freqVal -eq "0x00000dac") {
    $activeMode = "Sweet-Spot"
}

# Mode Switch Functions
function Set-IceCold {
    powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0
    powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0
    powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0
    powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0
    powercfg /setactive SCHEME_CURRENT
    [System.Media.SystemSounds]::Asterisk.Play()
    [System.Windows.Forms.MessageBox]::Show("ICE-COLD MODE ACTIVATED!`n`n- CPU Clock: Base Frequency (~2.4 GHz)`n- Thermals: ~60C - 65C`n- Fans: Silent & Keyboard stays cool`n- Best for: Study, Browsing, Daily Work, Battery Saving", "CoolBoost Control", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)
    $form.Close()
}

function Set-SweetSpot {
    powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 3500
    powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 3500
    powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 4
    powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 4
    powercfg /setactive SCHEME_CURRENT
    [System.Media.SystemSounds]::Asterisk.Play()
    [System.Windows.Forms.MessageBox]::Show("SWEET-SPOT BALANCED MODE ACTIVATED!`n`n- CPU Clock: Capped at 3.5 GHz (Sweet Spot)`n- Thermals: ~72C - 78C`n- High FPS without 95C overheating!`n- Best for: Valorant, Competitive Gaming, Multitasking", "CoolBoost Control", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)
    $form.Close()
}

function Set-BeastTurbo {
    powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0
    powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0
    powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 2
    powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 2
    powercfg /setactive SCHEME_CURRENT
    [System.Media.SystemSounds]::Asterisk.Play()
    [System.Windows.Forms.MessageBox]::Show("BEAST TURBO MODE ACTIVATED!`n`n- CPU Clock: Full Uncapped Turbo Boost`n- Power: Max Wattage (High Thermals)`n- Best for: 4K Video Exports, Code Compiling, Heavy Benchmarks", "CoolBoost Control", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning)
    $form.Close()
}

# Create GUI
$form = New-Object System.Windows.Forms.Form
$form.Text = "CoolBoost Control - " + $cpuName
$form.Size = New-Object System.Drawing.Size(470, 390)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.TopMost = $true
$form.BackColor = [System.Drawing.Color]::FromArgb(248, 249, 250)

# Status Label
$lblStatus = New-Object System.Windows.Forms.Label
$lblStatus.Location = New-Object System.Drawing.Point(25, 15)
$lblStatus.Size = New-Object System.Drawing.Size(410, 30)
$lblStatus.Font = New-Object System.Drawing.Font("Segoe UI", 11, [System.Drawing.FontStyle]::Bold)
$lblStatus.Text = "Current Active Mode: " + $activeMode.ToUpper()
$lblStatus.ForeColor = [System.Drawing.Color]::FromArgb(33, 37, 41)
$form.Controls.Add($lblStatus)

# Button 1: Ice-Cold
$btnCold = New-Object System.Windows.Forms.Button
$btnCold.Location = New-Object System.Drawing.Point(25, 55)
$btnCold.Size = New-Object System.Drawing.Size(405, 70)
$btnCold.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$btnCold.BackColor = [System.Drawing.Color]::FromArgb(227, 242, 253)
$btnCold.ForeColor = [System.Drawing.Color]::FromArgb(13, 71, 161)
$btnCold.FlatStyle = "Flat"
$btnCold.Text = "1. ICE-COLD MODE  [Base Clock | ~60C - 65C]`nSilent Fans - Zero Heating - Maximum Battery`n(Best for: Normal Study, Browsing, Daily Work)"
$btnCold.Add_Click({ Set-IceCold })
$form.Controls.Add($btnCold)

# Button 2: Sweet-Spot
$btnSweet = New-Object System.Windows.Forms.Button
$btnSweet.Location = New-Object System.Drawing.Point(25, 135)
$btnSweet.Size = New-Object System.Drawing.Size(405, 70)
$btnSweet.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$btnSweet.BackColor = [System.Drawing.Color]::FromArgb(232, 245, 233)
$btnSweet.ForeColor = [System.Drawing.Color]::FromArgb(27, 94, 32)
$btnSweet.FlatStyle = "Flat"
$btnSweet.Text = "2. SWEET-SPOT BALANCED  [3.5 GHz | ~72C - 78C]`nHigh FPS - No Throttling - Controlled Thermals`n(Best for: Valorant, Competitive Gaming, Multitasking)"
$btnSweet.Add_Click({ Set-SweetSpot })
$form.Controls.Add($btnSweet)

# Button 3: Beast Turbo
$btnBeast = New-Object System.Windows.Forms.Button
$btnBeast.Location = New-Object System.Drawing.Point(25, 215)
$btnBeast.Size = New-Object System.Drawing.Size(405, 70)
$btnBeast.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$btnBeast.BackColor = [System.Drawing.Color]::FromArgb(255, 235, 238)
$btnBeast.ForeColor = [System.Drawing.Color]::FromArgb(183, 28, 28)
$btnBeast.FlatStyle = "Flat"
$btnBeast.Text = "3. BEAST TURBO  [Max Uncapped | Full Power]`n100% Uncapped Boost - Max Wattage & Thermals`n(Best for: Heavy Video Rendering, Code Compiling)"
$btnBeast.Add_Click({ Set-BeastTurbo })
$form.Controls.Add($btnBeast)

# Footer info
$lblFooter = New-Object System.Windows.Forms.Label
$lblFooter.Location = New-Object System.Drawing.Point(25, 300)
$lblFooter.Size = New-Object System.Drawing.Size(410, 35)
$lblFooter.Font = New-Object System.Drawing.Font("Segoe UI", 8, [System.Drawing.FontStyle]::Regular)
$lblFooter.ForeColor = [System.Drawing.Color]::Gray
$lblFooter.Text = "CoolBoost Control by Anurag Kumar | Open-Source on GitHub`nApplies changes dynamically to the active Windows power scheme."
$form.Controls.Add($lblFooter)

[void]$form.ShowDialog()
