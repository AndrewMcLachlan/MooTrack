<#
.SYNOPSIS
    Reports whether the MooTrack agent is running and actually capturing.

.DESCRIPTION
    Read-only. Changes nothing. A service reporting Running is not evidence that
    observations are being written, so this checks the journal itself.
#>
[CmdletBinding()]
param(
    [string] $DataPath = (Join-Path $env:ProgramData 'MooTrack'),
    [string] $ServiceName = 'MooTrack'
)

$journalPath = Join-Path $DataPath 'journal'

Write-Host "`nMooTrack agent" -ForegroundColor Cyan

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "  service  : not installed" -ForegroundColor Red
}
else {
    $colour = if ($service.Status -eq 'Running') { 'Green' } else { 'Red' }
    Write-Host "  service  : $($service.Status)" -ForegroundColor $colour
}

if (-not (Test-Path $journalPath)) {
    Write-Host "  journal  : $journalPath does not exist" -ForegroundColor Red
}
else {
    $files = @(Get-ChildItem $journalPath -Filter *.ndjson -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime)

    if ($files.Count -eq 0) {
        Write-Host "  journal  : no day files yet" -ForegroundColor Yellow
    }
    else {
        $newest = $files[-1]
        $age = (Get-Date) - $newest.LastWriteTime
        $colour = if ($age.TotalMinutes -lt 5) { 'Green' } else { 'Yellow' }

        Write-Host "  journal  : $($files.Count) day files in $journalPath"
        Write-Host "  newest   : $($newest.Name), written $([int]$age.TotalMinutes) min ago" -ForegroundColor $colour

        $lines = Get-Content $newest.FullName
        Write-Host "  today    : $($lines.Count) observations"

        $byEvent = $lines |
            ForEach-Object { try { ($_ | ConvertFrom-Json).event } catch { 'unparseable' } } |
            Group-Object |
            Sort-Object Count -Descending

        foreach ($group in $byEvent) {
            Write-Host ("             {0,-14} {1}" -f $group.Name, $group.Count)
        }
    }
}

$shipped = Join-Path $DataPath 'shipped.json'
if (Test-Path $shipped) {
    Write-Host "  shipped  : $(Get-Content $shipped -Raw)".TrimEnd()
}
else {
    Write-Host "  shipped  : nothing sent to a collector yet"
}

Write-Host "`nRecent log entries" -ForegroundColor Cyan
try {
    $entries = Get-EventLog -LogName Application -Source 'MooTrack' -Newest 10 -ErrorAction Stop
    if ($entries) {
        foreach ($entry in $entries) {
            $colour = switch ($entry.EntryType) {
                'Error' { 'Red' }
                'Warning' { 'Yellow' }
                default { 'Gray' }
            }
            Write-Host ("  {0:HH:mm:ss} {1}" -f $entry.TimeGenerated, $entry.EntryType) -ForegroundColor $colour

            # The EventLog provider puts "Category:" on the first line and the actual
            # message and exception below it, so printing one line hides the fault.
            foreach ($line in ($entry.Message -split "`r?`n")) {
                if ($line.Trim()) { Write-Host "      $($line.TrimEnd())" }
            }
        }
    }
    else {
        Write-Host "  none"
    }
}
catch {
    Write-Host "  no entries yet"
}

Write-Host ""
