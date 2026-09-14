<#
.SYNOPSIS
    Installs the MooTrack agent as a Windows service without Windows Installer.

.DESCRIPTION
    Does what MooTrack.msi does, using only service and file operations, for
    machines where policy blocks msiexec. Safe to re-run: an existing service is
    stopped, updated and restarted.

    The journal is never touched. It is the substantiation record and outlives
    the software that writes it.

.EXAMPLE
    .\Install-MooTrackAgent.ps1

.EXAMPLE
    .\Install-MooTrackAgent.ps1 -CollectorUrl https://collector:8088 -ApiKey 'secret'
#>
[CmdletBinding()]
param(
    [string] $SourcePath,
    [string] $InstallPath = (Join-Path $env:ProgramFiles 'MooTrack'),
    [string] $DataPath = (Join-Path $env:ProgramData 'MooTrack'),
    [string] $ServiceName = 'MooTrack',
    [string] $CollectorUrl,
    [string] $ApiKey,
    [string] $User = $env:USERNAME
)

$ErrorActionPreference = 'Stop'

function Write-Step { param($Message) Write-Host "  $Message" }

# Creating a service and writing under Program Files both require elevation.
# Failing here is clearer than failing halfway through a partial install.
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this from an elevated PowerShell session (Run as administrator).'
}

if (-not $SourcePath) {
    $candidates = @(
        $PSScriptRoot,
        (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\agent')
    )
    $SourcePath = $candidates |
        Where-Object { $_ -and (Test-Path (Join-Path $_ 'MooTrack.Agent.exe')) } |
        Select-Object -First 1

    if (-not $SourcePath) {
        throw 'MooTrack.Agent.exe not found beside this script, and no -SourcePath was given.'
    }
}

$exeSource = Join-Path $SourcePath 'MooTrack.Agent.exe'
if (-not (Test-Path $exeSource)) { throw "Not found: $exeSource" }

# A file copied from another machine carries a mark-of-the-web that Windows uses to
# refuse to run it, surfacing much later as a service that will not start.
Get-ChildItem $SourcePath -File | Unblock-File -ErrorAction SilentlyContinue

$exeTarget = Join-Path $InstallPath 'MooTrack.Agent.exe'
$journalPath = Join-Path $DataPath 'journal'
$configPath = Join-Path $DataPath 'appsettings.json'

Write-Host "`nMooTrack agent" -ForegroundColor Cyan
Write-Host "  source   : $SourcePath"
Write-Host "  install  : $InstallPath"
Write-Host "  data     : $DataPath`n"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Step "Stopping existing service"
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        $existing.WaitForStatus('Stopped', '00:00:30')
    }
}

Write-Step "Creating directories"
New-Item -ItemType Directory -Force -Path $InstallPath, $journalPath | Out-Null

Write-Step "Copying binaries"
Copy-Item -Path (Join-Path $SourcePath 'MooTrack.Agent.exe') -Destination $InstallPath -Force
$shippedConfig = Join-Path $SourcePath 'appsettings.json'
if (Test-Path $shippedConfig) {
    Copy-Item -Path $shippedConfig -Destination $InstallPath -Force
}

# This copy lives with the data, not the binaries, so reinstalling or upgrading
# cannot discard the collector URL and API key.
if ($CollectorUrl -or $ApiKey -or -not (Test-Path $configPath)) {
    Write-Step "Writing $configPath"
    $settings = [ordered]@{
        MooTrack = [ordered]@{
            JournalRoot  = $journalPath
            BookmarkPath = (Join-Path $DataPath 'bookmarks.json')
            ShipmentPath = (Join-Path $DataPath 'shipped.json')
            CollectorUrl = $CollectorUrl
            ApiKey       = $ApiKey
            User         = $User
        }
    }
    $settings | ConvertTo-Json -Depth 4 | Set-Content -Path $configPath -Encoding UTF8
}

Write-Step "Registering event log source"
try {
    if (-not [System.Diagnostics.EventLog]::SourceExists('MooTrack')) {
        New-EventLog -LogName Application -Source 'MooTrack'
    }
}
catch {
    Write-Warning "Could not create the event log source: $($_.Exception.Message)"
    Write-Warning "The service will still run; it just logs less usefully."
}

if ($existing) {
    Write-Step "Updating service configuration"
    & sc.exe config $ServiceName binPath= "`"$exeTarget`"" start= auto obj= LocalSystem | Out-Null
}
else {
    Write-Step "Creating service"
    New-Service -Name $ServiceName `
        -BinaryPathName "`"$exeTarget`"" `
        -DisplayName 'MooTrack working hours capture' `
        -Description 'Records screen-on, user presence and session events for working-hours substantiation.' `
        -StartupType Automatic | Out-Null
}

Write-Step "Setting recovery actions"
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

Write-Step "Starting service"
Start-Service -Name $ServiceName
(Get-Service -Name $ServiceName).WaitForStatus('Running', '00:00:30')

Write-Host "`nVerifying" -ForegroundColor Cyan
$service = Get-Service -Name $ServiceName
Write-Host "  status   : $($service.Status)"

$deadline = (Get-Date).AddSeconds(90)
$written = $null
while ((Get-Date) -lt $deadline -and -not $written) {
    $written = Get-ChildItem $journalPath -Filter *.ndjson -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $written) { Start-Sleep -Seconds 5 }
}

if ($written) {
    Write-Host "  journal  : $($written.FullName)" -ForegroundColor Green
    Write-Host "  captured : $((Get-Content $written.FullName | Measure-Object -Line).Lines) observations"
    Write-Host "`nInstalled and capturing.`n" -ForegroundColor Green
}
else {
    Write-Warning "Service is $($service.Status) but nothing written to $journalPath yet."
    Write-Warning "The first tick takes up to 60 seconds. Check again, then look at:"
    Write-Warning "  Get-EventLog -LogName Application -Source MooTrack -Newest 20"
}
