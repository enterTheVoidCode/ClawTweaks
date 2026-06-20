# Fan-Panic-Start-IPF.ps1
# Re-enables the IPF Fan Participant (TFN1) and starts Intel IPF / DTT again,
# restoring the normal Intel thermal stack after Fan-Panic-Stop-IPF.ps1.

# --- self-elevate to admin ---
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList ('-ExecutionPolicy','Bypass','-NoProfile','-File',"`"$PSCommandPath`"")
    exit
}

$ErrorActionPreference = 'Continue'
Write-Host "=== START Intel IPF / DTT fan control ===" -ForegroundColor Cyan

# Re-enable the fan participant first so the framework finds it on start.
$fanId = 'ACPI\INTC106A\TFN1'
try {
    $dev = Get-PnpDevice -InstanceId $fanId -ErrorAction Stop
    if ($dev.Status -eq 'Disabled') {
        Enable-PnpDevice -InstanceId $fanId -Confirm:$false -ErrorAction Stop
        Write-Host ("  fan participant {0}: enabled" -f $fanId) -ForegroundColor Green
    } else {
        Write-Host ("  fan participant {0}: already enabled" -f $fanId) -ForegroundColor DarkGray
    }
} catch {
    Write-Host ("  fan participant {0}: enable FAILED - {1}" -f $fanId, $_.Exception.Message) -ForegroundColor Red
}

$services = 'dptftcs','ipfsvc'
foreach ($svc in $services) {
    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($null -eq $s) { Write-Host ("  service {0}: not found" -f $svc) -ForegroundColor DarkGray; continue }
    try {
        if ($s.Status -ne 'Running') {
            Start-Service -Name $svc -ErrorAction Stop
            Write-Host ("  service {0}: started" -f $svc) -ForegroundColor Green
        } else {
            Write-Host ("  service {0}: already running" -f $svc) -ForegroundColor DarkGray
        }
    } catch {
        Write-Host ("  service {0}: start FAILED - {1}" -f $svc, $_.Exception.Message) -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Done. Intel thermal stack restored." -ForegroundColor Yellow
Write-Host ""
Read-Host "Press Enter to close"
