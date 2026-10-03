@echo off
setlocal
title CalmDown - Uninstaller

echo ========================================================
echo   CalmDown - Uninstaller
echo ========================================================
echo.
echo Restoring stock Windows power settings...

powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0 >nul 2>&1
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0 >nul 2>&1
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 2 >nul 2>&1
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 2 >nul 2>&1
powercfg /setactive SCHEME_CURRENT >nul 2>&1

echo Removing application files...
set "TARGET_DIR=%LOCALAPPDATA%\CalmDown"
if exist "%TARGET_DIR%" rmdir /S /Q "%TARGET_DIR%"

echo Removing Desktop shortcut...
powershell.exe -NoProfile -Command ^
    "$desktop = [Environment]::GetFolderPath('Desktop'); " ^
    "$lnk = Join-Path $desktop 'CalmDown.lnk'; " ^
    "if (Test-Path $lnk) { Remove-Item $lnk -Force }"

echo.
echo ========================================================
echo   CalmDown has been completely uninstalled.
echo   Stock Windows power settings have been restored.
echo ========================================================
echo.
pause
