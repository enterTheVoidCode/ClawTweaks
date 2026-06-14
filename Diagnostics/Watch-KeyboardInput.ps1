# =============================================================================
# ClawTweaks Keyboard-Input Watcher
#
# Installs a low-level keyboard hook (WH_KEYBOARD_LL) and prints every key event
# the OS dispatches — including events INJECTED by ClawTweaks via InputInjector /
# SendInput. The injected flag is the whole point: it lets you tell apart the two
# very different failure modes behind "the hotkey does nothing":
#
#   * Keys ARE shown with INJECTED=yes  -> ClawTweaks's injection reaches the OS.
#       The problem is downstream: the focused app/game filters injected input
#       (common with anti-cheat), or the function you expected is bound to a
#       different key / needs a registered handler (e.g. media keys).
#
#   * Keys are NOT shown at all         -> the injection never reached the OS.
#       Look at the helper log line "Injected shortcut ... -> foreground: ..."
#       (InputInjector failed / fell back, wrong session or secure desktop, etc.).
#
# Usage:
#   Run in an ELEVATED PowerShell (the ClawTweaks helper is elevated; a non-admin
#   watcher may not observe its injected events):
#       powershell -ExecutionPolicy Bypass -File .\Watch-KeyboardInput.ps1
#
#   Then trigger your ClawTweaks hotkey and watch the output. Press Ctrl+C to stop.
#   A copy is written to the Desktop (ClawTweaks_KeyLog_<timestamp>.txt).
# =============================================================================

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host ""
    Write-Host "  *** WARNING: NOT running as Administrator ***" -ForegroundColor Red
    Write-Host "  *** Injected events from the elevated helper may be invisible here. ***" -ForegroundColor Red
    Write-Host "  *** Re-run from an elevated PowerShell for reliable results. ***" -ForegroundColor Red
    Write-Host ""
}

Add-Type -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

public static class KeyWatch
{
    public static ConcurrentQueue<string> Lines = new ConcurrentQueue<string>();

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    private const uint LLKHF_EXTENDED          = 0x01;
    private const uint LLKHF_LOWER_IL_INJECTED = 0x02;
    private const uint LLKHF_INJECTED          = 0x10;
    private const uint LLKHF_UP                = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private static readonly LowLevelKeyboardProc _proc = HookCallback; // keep delegate alive
    private static IntPtr _hook = IntPtr.Zero;
    private static Thread _thread;
    private static volatile bool _running;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);

    public static void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Pump) { IsBackground = true };
        _thread.Start();
    }

    public static void Stop() { _running = false; }

    private static void Pump()
    {
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            Lines.Enqueue("ERROR: SetWindowsHookEx failed, Win32=" + Marshal.GetLastWin32Error());
            return;
        }
        Lines.Enqueue("Hook installed. Listening for key events...");
        MSG msg;
        // The LL hook callback is dispatched on this thread while it pumps messages.
        while (_running && GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }
        UnhookWindowsHookEx(_hook);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            int m = (int)wParam;
            bool up       = (m == WM_KEYUP || m == WM_SYSKEYUP) || (k.flags & LLKHF_UP) != 0;
            bool injected = (k.flags & LLKHF_INJECTED) != 0;
            bool lowerIl  = (k.flags & LLKHF_LOWER_IL_INJECTED) != 0;
            bool ext      = (k.flags & LLKHF_EXTENDED) != 0;

            string inj = injected ? (lowerIl ? "yes(lowerIL)" : "yes") : "no";
            string line = string.Format(
                "{0:HH:mm:ss.fff}  {1,-4}  VK=0x{2:X2} {3,-13} SC=0x{4:X2}{5}  INJECTED={6}",
                DateTime.Now, up ? "UP" : "DOWN", k.vkCode, VkName(k.vkCode), k.scanCode,
                ext ? " EXT" : "    ", inj);
            Lines.Enqueue(line);
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static string VkName(uint vk)
    {
        switch (vk)
        {
            case 0x08: return "BACKSPACE";
            case 0x09: return "TAB";
            case 0x0D: return "ENTER";
            case 0x10: return "SHIFT";
            case 0x11: return "CTRL";
            case 0x12: return "ALT";
            case 0x13: return "PAUSE";
            case 0x1B: return "ESC";
            case 0x20: return "SPACE";
            case 0x21: return "PAGEUP";
            case 0x22: return "PAGEDOWN";
            case 0x23: return "END";
            case 0x24: return "HOME";
            case 0x25: return "LEFT";
            case 0x26: return "UP";
            case 0x27: return "RIGHT";
            case 0x28: return "DOWN";
            case 0x2C: return "PRINTSCRN";
            case 0x2D: return "INSERT";
            case 0x2E: return "DELETE";
            case 0x5B: return "LWIN";
            case 0x5C: return "RWIN";
            case 0xA0: return "LSHIFT";
            case 0xA1: return "RSHIFT";
            case 0xA2: return "LCTRL";
            case 0xA3: return "RCTRL";
            case 0xA4: return "LALT";
            case 0xA5: return "RALT";
            case 0xAD: return "MUTE";
            case 0xAE: return "VOL-";
            case 0xAF: return "VOL+";
            case 0xB0: return "MEDIA_NEXT";
            case 0xB1: return "MEDIA_PREV";
            case 0xB3: return "MEDIA_PLAY";
        }
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();          // 0-9
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();          // A-Z
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);              // F1-F24
        return "";
    }
}
'@

$logFile = Join-Path ([Environment]::GetFolderPath('Desktop')) ("ClawTweaks_KeyLog_{0:yyyyMMdd_HHmmss}.txt" -f (Get-Date))

Write-Host ("=" * 70) -ForegroundColor Cyan
Write-Host "  ClawTweaks Keyboard-Input Watcher" -ForegroundColor Cyan
Write-Host "  Trigger your hotkey now. INJECTED=yes => ClawTweaks's keys reach the OS." -ForegroundColor Cyan
Write-Host "  Log file: $logFile" -ForegroundColor DarkGray
Write-Host "  Press Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ("=" * 70) -ForegroundColor Cyan

[KeyWatch]::Start()

try {
    while ($true) {
        $line = $null
        while ([KeyWatch]::Lines.TryDequeue([ref]$line)) {
            $color = if ($line -match 'INJECTED=yes') { 'Green' } elseif ($line -match '^ERROR') { 'Red' } else { 'Gray' }
            Write-Host $line -ForegroundColor $color
            Add-Content -Path $logFile -Value $line
        }
        Start-Sleep -Milliseconds 40
    }
}
finally {
    [KeyWatch]::Stop()
    Write-Host ""
    Write-Host "  Stopped. Log saved to: $logFile" -ForegroundColor Yellow
}
