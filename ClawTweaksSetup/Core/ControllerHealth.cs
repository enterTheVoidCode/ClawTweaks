using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ClawTweaksSetup.Core
{
    public enum HealthVerdict { Unknown, Clean, Warning, Problem }

    /// <summary>Structured result of the controller-health probe.</summary>
    public sealed class HealthResult
    {
        public HealthVerdict Verdict { get; set; } = HealthVerdict.Unknown;
        public List<string> Problems { get; } = new List<string>();  // block / strongly recommend fixing
        public List<string> Warnings { get; } = new List<string>();  // worth noting, not blocking
        public List<string> Info { get; } = new List<string>();      // neutral facts for the summary

        // Raw parsed flags (also drive later phases, e.g. Center M guidance).
        public bool ClawPresent { get; set; }
        public int ClawNodes { get; set; }
        public string ClawMode { get; set; } = "unknown";
        public int XInputConnected { get; set; }
        public bool CenterMRunning { get; set; }
        public bool SteamFilterPresent { get; set; }
        public int VirtualPadCount { get; set; }
        public string VirtualPadName { get; set; }
    }

    /// <summary>
    /// Runs a fast, read-only PowerShell probe of the live controller topology and turns it into a
    /// clean / warning / problem verdict. This is the "HW controller health first" gate: if the
    /// native Claw controller isn't clean (missing, or MSI Center M is fighting it), the virtual
    /// mode can't work reliably. Full remediation guidance (Center M removal) lives in the phase.
    /// </summary>
    public static class ControllerHealth
    {
        public static HealthResult Probe()
        {
            var result = new HealthResult();
            string tempScript = null;
            try
            {
                tempScript = Path.Combine(Path.GetTempPath(), "ClawTweaks_Health_" + Guid.NewGuid().ToString("N") + ".ps1");
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

                using var proc = Process.Start(psi);
                string stdout = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(20000))
                {
                    try { proc.Kill(); } catch { }
                    result.Problems.Add("Controller probe timed out.");
                    result.Verdict = HealthVerdict.Warning;
                    return result;
                }

                Parse(stdout, result);
                Judge(result);
            }
            catch (Exception ex)
            {
                result.Problems.Add("Controller probe failed: " + ex.Message);
                result.Verdict = HealthVerdict.Warning;
            }
            finally
            {
                try { if (tempScript != null && File.Exists(tempScript)) File.Delete(tempScript); }
                catch { }
            }
            return result;
        }

        private static void Parse(string stdout, HealthResult r)
        {
            if (string.IsNullOrEmpty(stdout)) return;
            foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "CLAW_PRESENT": r.ClawPresent = val == "1"; break;
                    case "CLAW_NODES": int.TryParse(val, out int n); r.ClawNodes = n; break;
                    case "CLAW_MODE": r.ClawMode = val; break;
                    case "XINPUT_CONNECTED": int.TryParse(val, out int x); r.XInputConnected = x; break;
                    case "CENTERM_RUNNING": r.CenterMRunning = val == "1"; break;
                    case "STEAM_FILTER": r.SteamFilterPresent = val == "1"; break;
                    case "VIRTUAL_PAD": int.TryParse(val, out int v); r.VirtualPadCount = v; break;
                    case "VIRTUAL_PAD_NAME": r.VirtualPadName = val; break;
                }
            }
        }

        private static void Judge(HealthResult r)
        {
            if (!r.ClawPresent)
            {
                r.Problems.Add("MSI Claw controller not detected (VID_0DB0). Connect/enable the controller, or MSI Center M may have taken it over.");
            }
            else
            {
                r.Info.Add($"MSI Claw controller detected ({r.ClawNodes} interface node(s), mode: {r.ClawMode}).");
            }

            if (r.CenterMRunning)
            {
                // Non-blocking here: MSI Center M is only flagged during the initial health check.
                // The guided deactivation/removal happens in a later phase, AFTER the app is installed.
                r.Warnings.Add("MSI Center M is running. It can fight ClawTweaks for the controller and LED. This is handled after installation.");
            }

            if (r.SteamFilterPresent)
            {
                r.Warnings.Add("Steam's Xbox controller filter driver is present — a common cause of double input. Check Steam Input settings if you see doubled inputs.");
            }

            if (r.XInputConnected >= 2)
            {
                r.Warnings.Add($"{r.XInputConnected} XInput controllers are currently visible. Two or more while playing = double input.");
            }

            if (r.Problems.Count > 0) r.Verdict = HealthVerdict.Problem;
            else if (r.Warnings.Count > 0) r.Verdict = HealthVerdict.Warning;
            else r.Verdict = HealthVerdict.Clean;
        }

        // Fast, read-only. Emits KEY=VALUE lines only. One PnP enumeration, XInput slot poll, and a
        // couple of process/driver checks — designed to finish in ~1-2s.
        private const string Script = @"
$ErrorActionPreference = 'SilentlyContinue'
$pnp = @(Get-CimInstance Win32_PnPEntity)

$claw = @($pnp | Where-Object { $_.PNPDeviceID -match 'VID_0DB0' })
if ($claw.Count -gt 0) {
    Write-Output 'CLAW_PRESENT=1'
    Write-Output ('CLAW_NODES=' + $claw.Count)
    $hw = $claw | ForEach-Object { $_.HardwareID } | Where-Object { $_ }
    if ($hw -match 'PID_1902') { Write-Output 'CLAW_MODE=DInput' }
    elseif ($hw -match 'PID_1901') { Write-Output 'CLAW_MODE=XInput' }
    else { Write-Output 'CLAW_MODE=unknown' }
} else {
    Write-Output 'CLAW_PRESENT=0'
    Write-Output 'CLAW_NODES=0'
}

# Virtual gamepad = a Microsoft/Xbox controller that is NOT the physical Claw (VID_0DB0). VIIPER and
# ViGEm both present as a Microsoft Xbox pad (VID_045E). Its presence means a virtual pad is mounted.
$vpad = @($pnp | Where-Object { $_.PNPDeviceID -match 'VID_045E' -and $_.PNPDeviceID -notmatch 'VID_0DB0' -and ($_.Name -match 'Xbox|Controller') })
Write-Output ('VIRTUAL_PAD=' + $vpad.Count)
if ($vpad.Count -gt 0) { Write-Output ('VIRTUAL_PAD_NAME=' + $vpad[0].Name) }

# Live XInput slots
$xt = @'
using System;
using System.Runtime.InteropServices;
public class XiH {
    [StructLayout(LayoutKind.Sequential)] public struct GP { public ushort b; public byte lt, rt; public short lx, ly, rx, ry; }
    [StructLayout(LayoutKind.Sequential)] public struct ST { public uint pkt; public GP gp; }
    [DllImport(""xinput1_4.dll"", EntryPoint=""XInputGetState"")] public static extern uint GetState(uint i, ref ST s);
}
'@
try {
    if (-not ([System.Management.Automation.PSTypeName]'XiH').Type) { Add-Type -TypeDefinition $xt }
    $c = 0
    for ($i = 0; $i -lt 4; $i++) { $s = New-Object XiH+ST; if ([XiH]::GetState([uint32]$i, [ref]$s) -eq 0) { $c++ } }
    Write-Output ('XINPUT_CONNECTED=' + $c)
} catch { Write-Output 'XINPUT_CONNECTED=-1' }

# MSI Center M running?
$cm = @(Get-Process | Where-Object { $_.ProcessName -match 'MSI_Center|MSI Center|MSICenter' })
if ($cm.Count -gt 0) { Write-Output 'CENTERM_RUNNING=1' } else { Write-Output 'CENTERM_RUNNING=0' }

# Steam Xbox filter driver present?
$sx = @($pnp | Where-Object { $_.PNPDeviceID -match 'steamxbox' })
if ($sx.Count -gt 0) { Write-Output 'STEAM_FILTER=1' } else { Write-Output 'STEAM_FILTER=0' }
";
    }
}
