@echo off
setlocal EnableExtensions DisableDelayedExpansion

title ClawTweaks Installer

:: ── Store paths ───────────────────────────────────────────────
set "BAT_PATH=%~f0"
set "PS1_PATH=%~dp0_Installer\Install.ps1"

:: ── Admin check via fsutil ────────────────────────────────────
:: fsutil dirty query exits 0 only when caller has admin privileges.
:: No PowerShell round-trip needed, no output produced.
fsutil dirty query %SystemDrive% >nul 2>&1
if %errorlevel% == 0 goto :run

:: ── Not admin — re-launch elevated ───────────────────────────
echo Requesting Administrator privileges...
echo If a UAC prompt appears, click Yes to continue.
echo.
:: Pass path via environment variable to avoid quoting issues with spaces/special chars.
:: No try/catch: if UAC is cancelled the window simply closes (exit /b).
:: The elevated window takes over; this non-elevated window is no longer needed.
set "CT_SELF=%BAT_PATH%"
powershell.exe -NoProfile -WindowStyle Hidden -Command ^
    "Start-Process cmd.exe -ArgumentList ('/c',$env:CT_SELF) -Verb RunAs"
exit /b

:: ── Running as admin ─────────────────────────────────────────
:run
:: Set CWD to the BAT's own directory now that we are elevated.
:: (Mapped-drive CWD errors from CMD startup are suppressed via 2>nul.)
cd /d "%~dp0" 2>nul

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
