@echo off
setlocal

:: ============================================================
::  ClawTweaks Installer
::  Launches Install.ps1 with ExecutionPolicy Bypass so the
::  script runs regardless of system PowerShell policy.
:: ============================================================

title ClawTweaks Installer

:: Check for Administrator rights — re-launch elevated if needed
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting Administrator privileges...
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

:: Locate PowerShell
set "PS=powershell.exe"
where powershell.exe >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] PowerShell was not found on this system.
    echo         Please install PowerShell and try again.
    echo.
    goto :end
)

:: Run the installer script
echo.
echo Starting ClawTweaks installation...
echo.

"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0_Installer\Install.ps1" %*

set "EXIT_CODE=%errorlevel%"

if %EXIT_CODE% neq 0 (
    echo.
    echo =============================================
    echo   Installation failed  (exit code %EXIT_CODE%)
    echo =============================================
    echo.
    echo If you see a red error message above, please
    echo take a screenshot and report it on GitHub.
    echo.
) else (
    echo.
    echo =============================================
    echo   Installation complete!
    echo =============================================
    echo.
)

:end
echo Press any key to close this window...
pause >nul
endlocal
