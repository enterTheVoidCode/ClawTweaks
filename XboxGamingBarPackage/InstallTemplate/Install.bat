@echo off
setlocal EnableExtensions DisableDelayedExpansion

:: ============================================================
::  ClawTweaks Installer
::  Launches Install.ps1 with ExecutionPolicy Bypass so the
::  script runs regardless of system PowerShell policy.
::  All errors are caught and displayed — window never closes.
:: ============================================================

title ClawTweaks Installer

:: Store paths in variables early so special chars are handled once
set "BAT_PATH=%~f0"
set "BAT_DIR=%~dp0"
set "PS1_PATH=%~dp0_Installer\Install.ps1"

:: ── Elevation check ──────────────────────────────────────────
:: Use PowerShell for the admin check — more reliable than net session
:: which can fail on domain machines even when the user is admin.
:: Note: no ternary operator — must be compatible with PowerShell 5.1.
powershell.exe -NoProfile -Command "if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { exit 0 } else { exit 1 }" >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting Administrator privileges...
    echo If a UAC prompt appears, click Yes to continue.
    echo.
    :: Use cmd /c with fully quoted path to handle spaces and most special chars
    powershell.exe -NoProfile -Command "Start-Process -FilePath 'cmd.exe' -ArgumentList '/c \"%BAT_PATH%\"' -Verb RunAs -Wait" >nul 2>&1
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

:: ── PS1 existence check ──────────────────────────────────────
if not exist "%PS1_PATH%" (
    echo.
    echo [ERROR] Installer script not found:
    echo         %PS1_PATH%
    echo.
    echo         The ZIP may be incomplete. Please re-download and extract again.
    echo.
    goto :end
)

:: ── Run installer ────────────────────────────────────────────
echo.
echo Starting ClawTweaks installation...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS1_PATH%" -CalledFromBat

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
