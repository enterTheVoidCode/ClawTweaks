@echo off
setlocal

:: ============================================================
::  ClawTweaks Installer
::  Launches Install.ps1 with ExecutionPolicy Bypass so the
::  script runs regardless of system PowerShell policy.
::  All errors are caught and displayed — window never closes.
:: ============================================================

title ClawTweaks Installer

:: ── Elevation check ──────────────────────────────────────────
:: Re-launch this bat elevated if not already admin.
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting Administrator privileges...
    echo If a UAC prompt appears, click Yes to continue.
    echo.
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs -Wait" >nul 2>&1
    if %errorlevel% neq 0 (
        echo.
        echo [ERROR] Could not obtain Administrator privileges.
        echo         Please right-click Install.bat and select "Run as administrator".
        echo.
        goto :end
    )
    exit /b
)

:: ── PowerShell check ─────────────────────────────────────────
where powershell.exe >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] PowerShell was not found on this system.
    echo         Please install PowerShell 5.1 or later and try again.
    echo.
    goto :end
)

:: ── Run installer ────────────────────────────────────────────
echo.
echo Starting ClawTweaks installation...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0_Installer\Install.ps1" -CalledFromBat %*

set "EXIT_CODE=%errorlevel%"

:: ── Result ───────────────────────────────────────────────────
if %EXIT_CODE% neq 0 (
    echo.
    echo =============================================
    echo   Installation failed  ^(exit code %EXIT_CODE%^)
    echo =============================================
    echo.
    echo If you see an error message above, please
    echo take a screenshot and report it on GitHub:
    echo https://github.com/enterTheVoidCode/ClawTweaks/issues
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
