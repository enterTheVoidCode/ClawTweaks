using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using NLog;

namespace XboxGamingBarHelper.Diagnostics
{
    /// <summary>
    /// One-shot controller diagnostic dumper for the log-export feature. Runs a focused, read-only
    /// PowerShell script that captures everything relevant to hard-to-reproduce controller bugs
    /// (double input in Forza/etc.): all game-controller PnP devices + count, live XInput slots, the
    /// MSI Claw firmware mode/interfaces, HidHide hidden/whitelist state, ViGEm + usbip presence, and
    /// the Steam "Xbox Extended Feature Support" upper-filter driver. Written next to the widget/helper
    /// logs so a user can send back one extra file that shows the live controller topology.
    ///
    /// Deliberately does NOT run the slow pnputil driver dump — this must finish in a few seconds so
    /// the log export stays responsive.
    /// </summary>
    internal static class ControllerDiagnostics
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Runs the diagnostic script and writes the result to <paramref name="outFilePath"/>. A
        /// <paramref name="headerFromHelper"/> block (ClawTweaks' own known state, which the script can't
        /// read from the packaged LocalSettings) is prepended. Never throws — on failure it writes what
        /// it can plus the error, so the export always produces the file.
        /// </summary>
        public static void Collect(string outFilePath, string headerFromHelper)
        {
            string tempScript = null;
            try
            {
                // Prepend the helper-known state, then the PowerShell system dump.
                var sb = new StringBuilder();
                sb.AppendLine("==============================================================================");
                sb.AppendLine("  ClawTweaks Controller Diagnostics");
                sb.AppendLine("==============================================================================");
                sb.AppendLine("  Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();
                sb.AppendLine("  --- ClawTweaks state (from helper) ---");
                sb.AppendLine(headerFromHelper ?? "  (unavailable)");
                sb.AppendLine();
                File.WriteAllText(outFilePath, sb.ToString(), new UTF8Encoding(false));

                tempScript = Path.Combine(Path.GetTempPath(), "ClawTweaks_CtrlDiag_" + Guid.NewGuid().ToString("N") + ".ps1");
                File.WriteAllText(tempScript, Script, new UTF8Encoding(false));

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + tempScript + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc == null)
                    {
                        File.AppendAllText(outFilePath, "  (failed to start powershell.exe)\n", new UTF8Encoding(false));
                        return;
                    }
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    // Cap well under the widget's ExportLogs IPC timeout (60s) so a hung external tool
                    // can't make the whole export appear to fail. The script itself also guards each
                    // external call, so this ceiling is only a last resort.
                    if (!proc.WaitForExit(40000))
                    {
                        try { proc.Kill(); } catch { }
                        File.AppendAllText(outFilePath, "\n  (diagnostic script TIMED OUT after 40s — output above may be partial)\n", new UTF8Encoding(false));
                    }
                    File.AppendAllText(outFilePath, stdout ?? string.Empty, new UTF8Encoding(false));
                    if (!string.IsNullOrWhiteSpace(stderr))
                        File.AppendAllText(outFilePath, "\n  --- script stderr ---\n" + stderr + "\n", new UTF8Encoding(false));
                }

                Logger.Info($"Controller diagnostics written: {outFilePath}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Controller diagnostics collection failed: {ex.Message}");
                try { File.AppendAllText(outFilePath, "\n  (collection error: " + ex.Message + ")\n", new UTF8Encoding(false)); }
                catch { /* best effort */ }
            }
            finally
            {
                try { if (tempScript != null && File.Exists(tempScript)) File.Delete(tempScript); }
                catch { /* best effort */ }
            }
        }

        // Read-only, controller-focused PowerShell. Single-quoted strings used throughout to keep the
        // C# verbatim string simple. Every section is wrapped so one failing query never aborts the rest.
        private const string Script = @"
$ErrorActionPreference = 'Continue'
function Line($t){ Write-Output $t }
function Head($t){ Write-Output ''; Write-Output ('=' * 78); Write-Output ('  ' + $t); Write-Output ('=' * 78) }
function Sub($t){ Write-Output ''; Write-Output ('  --- ' + $t + ' ---') }
function Step($name,$block){ Sub $name; try { & $block } catch { Line ('    ERROR: ' + $_.Exception.Message) } }

$isAdmin = $false
try { $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) } catch {}

# Enumerate all PnP devices ONCE and reuse. Win32_PnPEntity is the slow part of this dump and was
# previously queried four separate times, which pushed the export past the widget's IPC timeout.
$script:pnp = @()
try { $script:pnp = @(Get-CimInstance Win32_PnPEntity -ErrorAction Stop) } catch { }

# Run an external tool with a hard timeout so a hung/slow HidHideCLI or usbip can never stall the
# whole export. Windows PowerShell 5.1 => use ProcessStartInfo.Arguments (no ArgumentList).
function RunExe($exe, $argLine, $timeoutMs) {
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $exe
        $psi.Arguments = $argLine
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $p = [System.Diagnostics.Process]::Start($psi)
        $out = $p.StandardOutput.ReadToEndAsync()
        $err = $p.StandardError.ReadToEndAsync()
        if (-not $p.WaitForExit($timeoutMs)) { try { $p.Kill() } catch {}; return ('(timed out after ' + $timeoutMs + 'ms)') }
        $text = $out.Result
        if ($err.Result) { $text += [Environment]::NewLine + $err.Result }
        return $text
    } catch { return ('(failed: ' + $_.Exception.Message + ')') }
}

Head 'SYSTEM'
Step 'OS' { $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop; Line ('    ' + $os.Caption + ' build ' + $os.BuildNumber + ' (' + $os.OSArchitecture + ')') }
Step 'Machine' { $cs = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop; Line ('    ' + $cs.Manufacturer + ' / ' + $cs.Model) ; $p = Get-CimInstance Win32_ComputerSystemProduct -ErrorAction Stop; Line ('    product: ' + $p.Name) }
Line ('    elevated: ' + $isAdmin)

Head 'GAME CONTROLLERS  (double-input = more than one visible to games)'
Step 'Controller / gamepad PnP devices' {
    $ctrl = $script:pnp | Where-Object {
        $_.Name -match 'Controller|Joystick|Gamepad|Xbox|XInput|HID-compliant game' -or
        $_.PNPDeviceID -match 'VID_0DB0|IG_|VID_045E&PID_028E|VID_28DE'
    } | Sort-Object PNPDeviceID
    if (@($ctrl).Count -eq 0) { Line '    (none found)'; return }
    foreach ($d in $ctrl) {
        $tag = ''
        if ($d.PNPDeviceID -match 'VID_045E.*PID_028E') { $tag = '  <<< ViGEm virtual Xbox360 >>>' }
        elseif ($d.PNPDeviceID -match 'VID_0DB0')        { $tag = '  [MSI physical]' }
        elseif ($d.PNPDeviceID -match 'VID_28DE')        { $tag = '  [Valve/Steam]' }
        Line ('    [' + $d.Status + '] ' + $d.Name + $tag)
        Line ('        ' + $d.PNPDeviceID)
    }
    Line ('    --> total controller nodes: ' + @($ctrl).Count)
}

Step 'Live XInput slots (xinput1_4.dll)' {
    $xt = @'
using System;
using System.Runtime.InteropServices;
public class XiDiag {
    [StructLayout(LayoutKind.Sequential)] public struct GP { public ushort b; public byte lt, rt; public short lx, ly, rx, ry; }
    [StructLayout(LayoutKind.Sequential)] public struct ST { public uint pkt; public GP gp; }
    [DllImport(""xinput1_4.dll"", EntryPoint=""XInputGetState"")] public static extern uint GetState(uint i, ref ST s);
}
'@
    if (-not ([System.Management.Automation.PSTypeName]'XiDiag').Type) { Add-Type -TypeDefinition $xt }
    $connected = 0
    for ($i = 0; $i -lt 4; $i++) {
        $s = New-Object XiDiag+ST
        $r = [XiDiag]::GetState([uint32]$i, [ref]$s)
        if ($r -eq 0) { Line ('    Slot ' + $i + ' : CONNECTED (packet=' + $s.pkt + ')'); $connected++ }
        else          { Line ('    Slot ' + $i + ' : empty') }
    }
    Line ('    --> connected XInput slots: ' + $connected + '  (2+ while playing = double input)')
}

Head 'MSI CLAW CONTROLLER  (firmware mode / interfaces)'
Step 'VID_0DB0 firmware + mode' {
    $cim = $script:pnp | Where-Object { $_.PNPDeviceID -match 'VID_0DB0' }
    if (@($cim).Count -eq 0) { Line '    (no VID_0DB0 controller present)'; return }
    $allHw = $cim | ForEach-Object { $_.HardwareID } | Where-Object { $_ }
    $rev = $null
    foreach ($h in $allHw) { if ($h -match 'REV_([0-9A-Fa-f]{4})') { $rev = $matches[1].ToUpper(); break } }
    if ($rev) { Line ('    Controller firmware (bcdDevice): 0x' + $rev) } else { Line '    Controller firmware: (REV not found)' }
    $has1901 = [bool]($allHw -match 'PID_1901')
    $has1902 = [bool]($allHw -match 'PID_1902')
    if ($has1902) { Line '    Mode: DInput (PID_1902 present)' } elseif ($has1901) { Line '    Mode: XInput (PID_1901 only)' }
    $hasFFA0 = [bool]($allHw -match 'FFA0'); $hasFFF0 = [bool]($allHw -match 'FFF0')
    Line ('    Command interface UP:FFA0: ' + $(if ($hasFFA0) { 'present' } else { 'not in capture' }))
    Line ('    DInput gamepad UP:FFF0:   ' + $(if ($hasFFF0) { 'present' } else { 'not present' }))
    Line ('    VID_0DB0 nodes: ' + @($cim).Count)
    foreach ($d in ($cim | Sort-Object PNPDeviceID)) { Line ('      [' + $d.Status + '] ' + $d.Name + '  ' + $d.PNPDeviceID) }
}

Head 'HIDHIDE  (must HIDE the physical pad while the virtual one is active)'
Step 'HidHideCLI state' {
    $cli = @(
        'C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe',
        'C:\Program Files\Nefarius Software Solutions e.U\HidHide\x64\HidHideCLI.exe',
        'C:\Program Files\HidHide\x64\HidHideCLI.exe'
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $cli) { Line '    HidHideCLI.exe not found (HidHide may not be installed)'; return }
    Line ('    CLI: ' + $cli)
    foreach ($a in @('--dev-gaming','--app-list')) {
        Line ('    > HidHideCLI ' + $a)
        $out = RunExe $cli $a 6000
        foreach ($l in ($out -split ""`r?`n"")) { if ($l.Trim()) { Line ('      ' + $l) } }
    }
}

Head 'VIRTUAL CONTROLLER BACKENDS'
Step 'ViGEm bus' {
    $vigem = $script:pnp | Where-Object { $_.Name -match 'Nefarius Virtual|ViGEm' -or $_.PNPDeviceID -match 'ROOT\\VIGEM|Nefarius' }
    if (@($vigem).Count -eq 0) { Line '    ViGEm bus device not found' } else { foreach ($d in $vigem) { Line ('    [' + $d.Status + '] ' + $d.Name) } }
}
Step 'usbip (VIIPER backend)' {
    $usbip = @('C:\Program Files\usbip-win2\usbip.exe','C:\Program Files\usbipd-win\usbip.exe') | Where-Object { Test-Path $_ } | Select-Object -First 1
    $svc = Get-Service -Name 'usbip*','vhci*' -ErrorAction SilentlyContinue
    if ($svc) { foreach ($s in $svc) { Line ('    service ' + $s.Name + ': ' + $s.Status) } } else { Line '    (no usbip/vhci service found)' }
    if ($usbip) {
        Line ('    > usbip port'); $out = RunExe $usbip 'port' 6000; foreach ($l in ($out -split ""`r?`n"")) { if ($l.Trim()) { Line ('      ' + $l) } }
    } else { Line '    usbip.exe not found' }
}

Head 'STEAM INPUT  (a common double-input / interception source)'
Step 'Steam Xbox Extended Feature Support driver (steamxbox)' {
    $sx = $script:pnp | Where-Object { $_.Name -match 'Steam' -and $_.Name -match 'Xbox|Controller' -or $_.PNPDeviceID -match 'steamxbox' }
    if (@($sx).Count -eq 0) { Line '    steamxbox / Steam controller filter: not detected' } else { foreach ($d in $sx) { Line ('    [' + $d.Status + '] ' + $d.Name + '  ' + $d.PNPDeviceID) } }
    $drv = Get-CimInstance Win32_SystemDriver -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'steamxbox|steamvhid|steamstreaming' }
    if ($drv) { foreach ($d in $drv) { Line ('    driver ' + $d.Name + ': ' + $d.State + '/' + $d.StartMode) } }
}

Head 'RELEVANT PROCESSES'
Step 'Steam / MSI Center / gamepad-related' {
    $p = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'steam|MSI_Center|MSI Center|Nahimic|DS4Windows|x360ce|reWASD|HidHide|ViGEm|usbip|vgamepad' }
    if (@($p).Count -eq 0) { Line '    (none of the watched processes are running)'; return }
    foreach ($pp in ($p | Sort-Object ProcessName -Unique)) { Line ('    ' + $pp.ProcessName + '  (PID ' + $pp.Id + ')') }
}

Write-Output ''
Write-Output '  --- end of controller diagnostics ---'
";
    }
}
