@echo off
setlocal enabledelayedexpansion
title CoolBoost Control - Installer

echo ========================================================
echo   CoolBoost Control - Setup
echo   Thermal & Performance Switcher for Windows Laptops
echo ========================================================
echo.

set "TARGET_DIR=%LOCALAPPDATA%\CoolBoostControl"
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

echo [1/3] Copying application files to %TARGET_DIR%...
copy /Y "%~dp0src\Power_Mode_Selector.ps1" "%TARGET_DIR%\Power_Mode_Selector.ps1" >nul
if errorlevel 1 (
    echo [ERROR] Failed to copy files. Please ensure you extract the ZIP before running.
    pause
    exit /b 1
)

echo [2/3] Creating Desktop Shortcut with Hotkey (Ctrl+Alt+C)...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    "$WshShell = New-Object -ComObject WScript.Shell; " ^
    "$desktop = [Environment]::GetFolderPath('Desktop'); " ^
    "$shortcut = $WshShell.CreateShortcut((Join-Path $desktop 'CoolBoost Control.lnk')); " ^
    "$shortcut.TargetPath = 'powershell.exe'; " ^
    "$shortcut.Arguments = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File ""%TARGET_DIR%\Power_Mode_Selector.ps1""'; " ^
    "$shortcut.WorkingDirectory = '%TARGET_DIR%'; " ^
    "$shortcut.Description = 'Thermal & Performance Mode Switcher (Ice-Cold, Sweet-Spot, Beast)'; " ^
    "$shortcut.IconLocation = 'shell32.dll, 221'; " ^
    "$shortcut.Hotkey = 'Ctrl+Alt+C'; " ^
    "$shortcut.Save();"

echo [3/3] Setting initial mode to Ice-Cold Mode...
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0 >nul 2>&1
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0 >nul 2>&1
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0 >nul 2>&1
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0 >nul 2>&1
powercfg /setactive SCHEME_CURRENT >nul 2>&1

echo.
echo ========================================================
echo   SUCCESS! CoolBoost Control is installed.
echo ========================================================
echo.
echo  * Desktop shortcut created: 'CoolBoost Control'
echo  * Shortcut hotkey assigned: Ctrl + Alt + C
echo.
echo Press any key to launch CoolBoost Control now...
pause >nul
start "" powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%TARGET_DIR%\Power_Mode_Selector.ps1"
exit /b 0
