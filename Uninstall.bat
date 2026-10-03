@echo off
setlocal
title CoolBoost Control - Uninstaller

echo ========================================================
echo   CoolBoost Control - Uninstaller
echo ========================================================
echo.
echo Restoring stock Windows power settings...

powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0 >nul 2>&1
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0 >nul 2>&1
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 2 >nul 2>&1
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 2 >nul 2>&1
powercfg /setactive SCHEME_CURRENT >nul 2>&1

echo Removing application files...
set "TARGET_DIR=%LOCALAPPDATA%\CoolBoostControl"
if exist "%TARGET_DIR%" rmdir /S /Q "%TARGET_DIR%"

echo Removing Desktop shortcut...
powershell.exe -NoProfile -Command ^
    "$desktop = [Environment]::GetFolderPath('Desktop'); " ^
    "$lnk = Join-Path $desktop 'CoolBoost Control.lnk'; " ^
    "if (Test-Path $lnk) { Remove-Item $lnk -Force }"

echo.
echo ========================================================
echo   CoolBoost Control has been completely uninstalled.
echo   Stock Windows power settings have been restored.
echo ========================================================
echo.
pause
