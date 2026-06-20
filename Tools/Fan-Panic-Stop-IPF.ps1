# Fan-Panic-Stop-IPF.ps1
# Stops Intel IPF / DTT and disables the IPF Fan Participant (TFN1) so the EC
# fan table regains sole control. Run this when the fan is stuck in panic mode.
# Re-enable everything afterwards with Fan-Panic-Start-IPF.ps1.

# --- self-elevate to admin ---
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList ('-ExecutionPolicy','Bypass','-NoProfile','-File',"`"$PSCommandPath`"")
    exit
}

$ErrorActionPreference = 'Continue'
Write-Host "=== STOP Intel IPF / DTT fan control ===" -ForegroundColor Cyan

$services = 'ipfsvc','dptftcs'
foreach ($svc in $services) {
    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($null -eq $s) { Write-Host ("  service {0}: not found" -f $svc) -ForegroundColor DarkGray; continue }
    try {
        if ($s.Status -ne 'Stopped') {
            Stop-Service -Name $svc -Force -ErrorAction Stop
            Write-Host ("  service {0}: stopped" -f $svc) -ForegroundColor Green
        } else {
            Write-Host ("  service {0}: already stopped" -f $svc) -ForegroundColor DarkGray
        }
    } catch {
        Write-Host ("  service {0}: stop FAILED - {1}" -f $svc, $_.Exception.Message) -ForegroundColor Red
    }
}

# IPF Fan Participant - the device IPF uses to command the fan directly.
$fanId = 'ACPI\INTC106A\TFN1'
try {
    $dev = Get-PnpDevice -InstanceId $fanId -ErrorAction Stop
    if ($dev.Status -ne 'Disabled') {
        Disable-PnpDevice -InstanceId $fanId -Confirm:$false -ErrorAction Stop
        Write-Host ("  fan participant {0}: disabled" -f $fanId) -ForegroundColor Green
    } else {
        Write-Host ("  fan participant {0}: already disabled" -f $fanId) -ForegroundColor DarkGray
    }
} catch {
    Write-Host ("  fan participant {0}: disable FAILED - {1}" -f $fanId, $_.Exception.Message) -ForegroundColor Red
}

Write-Host ""
Write-Host "Done. Watch the fan: if it drops now, IPF/TFN1 was the panic source." -ForegroundColor Yellow
Write-Host "Re-enable with Fan-Panic-Start-IPF.ps1 when finished." -ForegroundColor Yellow
Write-Host ""
Read-Host "Press Enter to close"
