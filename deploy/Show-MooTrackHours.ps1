<#
.SYNOPSIS
    Derives hours from the local journal and prints them.

.DESCRIPTION
    Read-only with respect to the journal. Writes the derived CSVs and workbook to
    an output folder, then prints the daily table.

    This is the same engine the collector runs, so the figures match what the NAS
    will produce once it is deployed.

.EXAMPLE
    .\Show-MooTrackHours.ps1

.EXAMPLE
    .\Show-MooTrackHours.ps1 -BridgeMinutes 5
#>
[CmdletBinding()]
param(
    [string] $JournalPath = (Join-Path $env:ProgramData 'MooTrack\journal'),
    [string] $OutputPath = (Join-Path $env:USERPROFILE 'MooTrack-hours'),
    [int] $BridgeMinutes = 10
)

$ErrorActionPreference = 'Stop'

$engine = Join-Path $PSScriptRoot 'MooTrack.Cli.exe'
if (-not (Test-Path $engine)) { throw "MooTrack.Cli.exe not found beside this script." }

if (-not (Test-Path $JournalPath)) { throw "No journal at $JournalPath" }

Unblock-File $engine -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

& $engine --ndjson $JournalPath --out $OutputPath --bridge $BridgeMinutes
if ($LASTEXITCODE -ne 0) { throw "Derivation failed with exit code $LASTEXITCODE" }

$daily = Join-Path $OutputPath 'daily-hours.csv'
if (Test-Path $daily) {
    Write-Host "`nDaily" -ForegroundColor Cyan
    Import-Csv $daily |
        Select-Object date, weekday,
            @{n='first';e={$_.first_active}},
            @{n='last';e={$_.last_active}},
            @{n='active';e={$_.active_hours}},
            @{n='span';e={$_.span_hours}},
            @{n='breaks';e={$_.longest_break_min}},
            quality |
        Format-Table -AutoSize
}

Write-Host "Workbook: $(Join-Path $OutputPath 'mootrack-hours.xlsx')`n"
