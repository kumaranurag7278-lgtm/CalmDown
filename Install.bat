@echo off
setlocal enabledelayedexpansion
title CalmDown - Setup

echo ========================================================
echo   CalmDown - Setup
echo   Driver-Free CPU Thermal & Power Governor
echo ========================================================
echo.

set "TARGET_DIR=%LOCALAPPDATA%\CalmDown"
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

echo [1/2] Copying CalmDown.exe to %TARGET_DIR%...
if exist "%~dp0bin\CalmDown.exe" (
    copy /Y "%~dp0bin\CalmDown.exe" "%TARGET_DIR%\CalmDown.exe" >nul
) else if exist "%~dp0CalmDown.exe" (
    copy /Y "%~dp0CalmDown.exe" "%TARGET_DIR%\CalmDown.exe" >nul
) else (
    echo [ERROR] CalmDown.exe not found in "%~dp0" or "%~dp0bin\".
    pause
    exit /b 1
)

echo [2/2] Creating Desktop Shortcut with Hotkey (Ctrl+Alt+C)...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    "$WshShell = New-Object -ComObject WScript.Shell; " ^
    "$desktop = [Environment]::GetFolderPath('Desktop'); " ^
    "$shortcut = $WshShell.CreateShortcut((Join-Path $desktop 'CalmDown.lnk')); " ^
    "$shortcut.TargetPath = '%TARGET_DIR%\CalmDown.exe'; " ^
    "$shortcut.WorkingDirectory = '%TARGET_DIR%'; " ^
    "$shortcut.Description = 'CalmDown: Driver-Free Hardware Power Governor'; " ^
    "$shortcut.IconLocation = 'shell32.dll, 221'; " ^
    "$shortcut.Hotkey = 'Ctrl+Alt+C'; " ^
    "$shortcut.Save();"

echo.
echo ========================================================
echo   SUCCESS! CalmDown is installed.
echo ========================================================
echo.
echo  * Desktop shortcut created: 'CalmDown'
echo  * Shortcut hotkey assigned: Ctrl + Alt + C
echo  * Executable path: %TARGET_DIR%\CalmDown.exe
echo.
echo Press any key to launch CalmDown now...
pause >nul
start "" "%TARGET_DIR%\CalmDown.exe"
exit /b 0
