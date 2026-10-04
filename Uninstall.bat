@echo off
setlocal
title CalmDown - Uninstaller

echo ========================================================
echo   CalmDown - Uninstaller
echo ========================================================
echo.

set "TARGET_DIR=%LOCALAPPDATA%\CalmDown"

echo [1/3] Restoring original factory power settings...
if exist "%TARGET_DIR%\CalmDown.exe" (
    "%TARGET_DIR%\CalmDown.exe" --restore
)

echo [2/3] Terminating running CalmDown process...
taskkill /F /IM CalmDown.exe >nul 2>&1

echo [3/3] Removing application files and Desktop shortcut...
if exist "%TARGET_DIR%" rmdir /S /Q "%TARGET_DIR%"

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
