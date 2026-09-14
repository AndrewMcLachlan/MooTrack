<#
.SYNOPSIS
    Removes the MooTrack agent service and binaries.

.DESCRIPTION
    The journal and its position files are left in place. They are the
    substantiation record and are not the software's to delete. Remove them by
    hand if you genuinely mean to.
#>
[CmdletBinding()]
param(
    [string] $InstallPath = (Join-Path $env:ProgramFiles 'MooTrack'),
    [string] $DataPath = (Join-Path $env:ProgramData 'MooTrack'),
    [string] $ServiceName = 'MooTrack'
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this from an elevated PowerShell session (Run as administrator).'
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Write-Host "  Stopping service"
        Stop-Service -Name $ServiceName -Force
        $service.WaitForStatus('Stopped', '00:00:30')
    }

    Write-Host "  Removing service"
    if (Get-Command Remove-Service -ErrorAction SilentlyContinue) {
        Remove-Service -Name $ServiceName
    }
    else {
        & sc.exe delete $ServiceName | Out-Null
    }
}
else {
    Write-Host "  No service named $ServiceName"
}

if (Test-Path $InstallPath) {
    Write-Host "  Removing $InstallPath"
    Remove-Item -Path $InstallPath -Recurse -Force
}

try {
    if ([System.Diagnostics.EventLog]::SourceExists('MooTrack')) {
        Write-Host "  Removing event log source"
        Remove-EventLog -Source 'MooTrack'
    }
}
catch {
    Write-Warning "Could not remove the event log source: $($_.Exception.Message)"
}

$journalPath = Join-Path $DataPath 'journal'
if (Test-Path $journalPath) {
    $files = @(Get-ChildItem $journalPath -Filter *.ndjson -ErrorAction SilentlyContinue)
    Write-Host "`nKept: $journalPath ($($files.Count) day files)" -ForegroundColor Yellow
    Write-Host "Your recorded hours are still there. Delete them yourself if you mean to.`n"
}
