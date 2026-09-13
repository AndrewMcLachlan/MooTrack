<#
.SYNOPSIS
    Inventories every signal on this machine that could contribute to reconstructing working hours.

.DESCRIPTION
    Read-only. Changes nothing. Reports:
      A. Log inventory      - which logs exist, are enabled, and how far back they reach
      B. Signal density     - how many usable events per day, per signal, over the sample window
      C. Sample timeline    - a reconstructed day, so you can eyeball whether the data is usable
      D. Passive artefacts  - non-event sources that exist whether or not anything is running
      E. Verdict            - which signals are viable on THIS machine

    Run without elevation first. Re-run elevated to include the Security log
    and audit policy state; the script reports what it could not see.

.PARAMETER Days
    Sample window for density analysis. Default 14.

.PARAMETER TimelineDate
    Which day to reconstruct in section C. Defaults to yesterday.

.PARAMETER OutputDir
    Where CSV output is written. Default %TEMP%\worktime-survey.

.EXAMPLE
    .\Survey-WorkTimeSignals.ps1
    .\Survey-WorkTimeSignals.ps1 -Days 30 -TimelineDate (Get-Date).AddDays(-3)
#>

#Requires -Version 5.1
[CmdletBinding()]
param(
    [int]$Days = 14,
    [datetime]$TimelineDate = (Get-Date).Date.AddDays(-1),
    [string]$OutputDir = "$env:TEMP\worktime-survey"
)

$ErrorActionPreference = 'Stop'
$since = (Get-Date).Date.AddDays(-$Days)

$isElevated = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }

function Write-Section($title) {
    Write-Host ""
    Write-Host ("=" * 78) -ForegroundColor DarkCyan
    Write-Host "  $title" -ForegroundColor Cyan
    Write-Host ("=" * 78) -ForegroundColor DarkCyan
}

# ---------------------------------------------------------------------------
# Signal catalogue
# Kind:  edge     = precise state transition, the good stuff
#        envelope = bounds the day but coarse
#        presence = proves the machine was alive, no state implied
# ---------------------------------------------------------------------------
$signals = @(
    @{ Name='Workstation lock/unlock';      Log='Security'; Ids=@(4800,4801);          Kind='edge';     Note='Subcategory: Other Logon/Logoff Events' }
    @{ Name='Screensaver on/off';           Log='Security'; Ids=@(4802,4803);          Kind='edge';     Note='Same subcategory as 4800/4801' }
    @{ Name='Logon (all types)';            Log='Security'; Ids=@(4624);               Kind='edge';     Note='Type 2=interactive, 7=unlock, 11=cached' }
    @{ Name='Logoff';                       Log='Security'; Ids=@(4634,4647);          Kind='edge';     Note='4647 = user-initiated' }
    @{ Name='Process creation';             Log='Security'; Ids=@(4688);               Kind='presence'; Note='Very high volume if enabled' }

    @{ Name='Sleep / resume (kernel)';      Log='System';   Ids=@(42,107);             Kind='edge';     Note='Kernel-Power' }
    @{ Name='Unexpected shutdown';          Log='System';   Ids=@(41,6008);            Kind='edge';     Note='Crash / power loss' }
    @{ Name='Boot / shutdown (kernel)';     Log='System';   Ids=@(12,13);              Kind='edge';     Note='Kernel-General' }
    @{ Name='Shutdown initiated';           Log='System';   Ids=@(1074);               Kind='edge';     Note='User32, names the initiating process' }
    @{ Name='Event log service start/stop'; Log='System';   Ids=@(6005,6006,6013);     Kind='envelope'; Note='6013 = uptime snapshot' }
    @{ Name='Time change';                  Log='System';   Ids=@(1);                  Kind='edge';     Note='Kernel-General; clock jumps break naive maths' }

    @{ Name='Sleep/wake with source';       Log='Microsoft-Windows-Power-Troubleshooter/Operational'; Ids=@(1);                  Kind='edge';     Note='Single record holds BOTH sleep and wake time' }
    @{ Name='Session connect/disconnect';   Log='Microsoft-Windows-TerminalServices-LocalSessionManager/Operational'; Ids=@(21,22,23,24,25); Kind='edge'; Note='Covers console, not just RDP' }
    @{ Name='Boot / shutdown performance';  Log='Microsoft-Windows-Diagnostics-Performance/Operational'; Ids=@(100,200);        Kind='edge';     Note='Includes durations' }
    @{ Name='User profile load/unload';     Log='Microsoft-Windows-User Profile Service/Operational';    Ids=@(1,2,3,4);        Kind='envelope'; Note='Brackets the logon session' }
    @{ Name='Network connect/disconnect';   Log='Microsoft-Windows-NetworkProfile/Operational';          Ids=@(10000,10001);    Kind='presence'; Note='Good awake-proxy, fires on wake' }
    @{ Name='WLAN association';             Log='Microsoft-Windows-WLAN-AutoConfig/Operational';         Ids=@(8001,8003);      Kind='presence'; Note='Only if wireless' }
    @{ Name='Shell / Explorer activity';    Log='Microsoft-Windows-Shell-Core/Operational';              Ids=@();               Kind='presence'; Note='Any event counts as presence' }
    @{ Name='Scheduled task execution';     Log='Microsoft-Windows-TaskScheduler/Operational';           Ids=@(100,102);        Kind='presence'; Note='Disabled by default on many builds' }
    @{ Name='Winlogon operational';         Log='Microsoft-Windows-Winlogon/Operational';                Ids=@();               Kind='edge';     Note='Logon subscriber timings' }
)

# ---------------------------------------------------------------------------
# A. Log inventory
# ---------------------------------------------------------------------------
Write-Section "A. Log inventory"

$logNames = $signals.Log | Select-Object -Unique
$logInfo = foreach ($ln in $logNames) {
    $row = [ordered]@{
        Log = $ln; Exists = $false; Enabled = $null; MaxSizeMB = $null
        Records = $null; OldestRecord = $null; WindowDays = $null; Readable = $false
    }
    try {
        $cfg = Get-WinEvent -ListLog $ln -ErrorAction Stop
        $row.Exists    = $true
        $row.Enabled   = $cfg.IsEnabled
        $row.MaxSizeMB = [math]::Round($cfg.MaximumSizeInBytes / 1MB, 1)
        $row.Records   = $cfg.RecordCount
        if ($cfg.RecordCount -gt 0) {
            try {
                $oldest = Get-WinEvent -LogName $ln -MaxEvents 1 -Oldest -ErrorAction Stop
                $row.Readable     = $true
                $row.OldestRecord = $oldest.TimeCreated
                $row.WindowDays   = [math]::Round(((Get-Date) - $oldest.TimeCreated).TotalDays, 1)
            } catch { $row.Readable = $false }
        } else { $row.Readable = $true }
    } catch { }
    [pscustomobject]$row
}

$logInfo | Format-Table Log, Exists, Enabled, MaxSizeMB, Records, WindowDays, Readable -AutoSize

if (-not $isElevated) {
    Write-Host "NOT ELEVATED - Security log results will be empty. Re-run as admin for the full picture." -ForegroundColor Yellow
}

$churn = $logInfo | Where-Object { $_.Log -eq 'Security' -and $_.WindowDays -ne $null -and $_.WindowDays -lt 7 }
if ($churn) {
    Write-Host ("Security log only reaches back {0} days - backfill beyond that is impossible. Consider raising MaxSize." -f $churn.WindowDays) -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# B. Signal density
# ---------------------------------------------------------------------------
Write-Section "B. Signal density over the last $Days days"

$density = foreach ($s in $signals) {
    $row = [ordered]@{
        Signal = $s.Name; Kind = $s.Kind; Log = $s.Log
        Total = 0; PerDay = 0.0; DaysSeen = 0; Status = ''; Note = $s.Note
    }
    $filter = @{ LogName = $s.Log; StartTime = $since }
    if ($s.Ids.Count -gt 0) { $filter['Id'] = $s.Ids }
    try {
        $events = Get-WinEvent -FilterHashtable $filter -ErrorAction Stop
        $row.Total    = @($events).Count
        $row.DaysSeen = @($events | Group-Object { $_.TimeCreated.Date }).Count
        $row.PerDay   = [math]::Round($row.Total / $Days, 1)
        $row.Status   = if ($row.DaysSeen -ge ($Days * 0.5)) { 'VIABLE' }
                        elseif ($row.Total -gt 0)            { 'sparse' }
                        else                                 { 'empty' }
    } catch {
        $row.Status = if ($_.Exception.Message -match 'No events were found') { 'empty' }
                      elseif ($_.Exception.Message -match 'access|denied')    { 'no access' }
                      else                                                    { 'unavailable' }
    }
    [pscustomobject]$row
}

$density | Sort-Object @{e={switch($_.Status){'VIABLE'{0}'sparse'{1}'empty'{2}default{3}}}}, Signal |
    Format-Table Signal, Kind, Status, Total, PerDay, DaysSeen -AutoSize

$density | Export-Csv (Join-Path $OutputDir 'signal-density.csv') -NoTypeInformation

# ---------------------------------------------------------------------------
# C. Sample day timeline
# ---------------------------------------------------------------------------
Write-Section "C. Reconstructed timeline for $($TimelineDate.ToString('yyyy-MM-dd'))"

$dayStart = $TimelineDate.Date
$dayEnd   = $dayStart.AddDays(1)

$map = @{
    'Security|4800'='Lock'; 'Security|4801'='Unlock'; 'Security|4802'='ScreensaverOn'
    'Security|4803'='ScreensaverOff'; 'Security|4624'='Logon'; 'Security|4634'='Logoff'
    'Security|4647'='LogoffUser'
    'System|42'='Sleep'; 'System|107'='Resume'; 'System|41'='UncleanShutdown'
    'System|12'='Boot'; 'System|13'='Shutdown'; 'System|1074'='ShutdownInit'
    'System|6005'='LogServiceStart'; 'System|6006'='LogServiceStop'
}

$timeline = @()
foreach ($ln in @('Security','System','Microsoft-Windows-Power-Troubleshooter/Operational',
                  'Microsoft-Windows-TerminalServices-LocalSessionManager/Operational')) {
    try {
        $evts = Get-WinEvent -FilterHashtable @{ LogName=$ln; StartTime=$dayStart; EndTime=$dayEnd } -ErrorAction Stop |
                Where-Object { $map.ContainsKey("$ln|$($_.Id)") -or $ln -notin @('Security','System') }
        foreach ($e in $evts) {
            $key = "$ln|$($e.Id)"
            $timeline += [pscustomobject]@{
                Time   = $e.TimeCreated
                Event  = if ($map.ContainsKey($key)) { $map[$key] } else { "$($e.Id)" }
                Source = ($ln -split '/|-')[-1]
                Detail = if ($e.Id -eq 4624) { "type=$($e.Properties[8].Value)" }
                         elseif ($e.Id -eq 1074) { ($e.Message -split "`n")[0] }
                         else { '' }
            }
        }
    } catch { }
}

if ($timeline.Count -eq 0) {
    Write-Host "No events found for that date. Try a different -TimelineDate, or re-run elevated." -ForegroundColor Yellow
} else {
    $timeline = $timeline | Sort-Object Time
    $timeline | Format-Table @{n='Time';e={$_.Time.ToString('HH:mm:ss')}}, Event, Source, Detail -AutoSize

    $first = $timeline[0].Time
    $last  = $timeline[-1].Time
    Write-Host ""
    Write-Host ("  First signal : {0}" -f $first.ToString('HH:mm:ss'))
    Write-Host ("  Last signal  : {0}" -f $last.ToString('HH:mm:ss'))
    Write-Host ("  Raw span     : {0:hh\:mm}" -f ($last - $first))

    # Largest unobserved gaps - these are what a heartbeat would fill
    $gaps = for ($i = 1; $i -lt $timeline.Count; $i++) {
        $g = $timeline[$i].Time - $timeline[$i-1].Time
        if ($g.TotalMinutes -ge 20) {
            [pscustomobject]@{
                From = $timeline[$i-1].Time.ToString('HH:mm'); To = $timeline[$i].Time.ToString('HH:mm')
                Minutes = [math]::Round($g.TotalMinutes); After = $timeline[$i-1].Event
            }
        }
    }
    if ($gaps) {
        Write-Host ""
        Write-Host "  Unobserved gaps >= 20 min (what a heartbeat would resolve):" -ForegroundColor Yellow
        $gaps | Format-Table From, To, Minutes, After -AutoSize
    }

    $timeline | Export-Csv (Join-Path $OutputDir "timeline-$($dayStart.ToString('yyyy-MM-dd')).csv") -NoTypeInformation
}

# ---------------------------------------------------------------------------
# D. Passive artefacts
# ---------------------------------------------------------------------------
Write-Section "D. Passive artefacts (exist whether or not anything is running)"

$artefacts = @()

$srum = "$env:SystemRoot\System32\sru\SRUDB.dat"
$artefacts += [pscustomobject]@{
    Artefact = 'SRUM database'; Present = (Test-Path $srum)
    Detail = if (Test-Path $srum) {
        $f = Get-Item $srum -ErrorAction SilentlyContinue
        "$([math]::Round($f.Length/1MB,1)) MB, modified $($f.LastWriteTime.ToString('yyyy-MM-dd HH:mm'))"
    } else { 'not found' }
}

$ua = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist'
$uaCount = 0
if (Test-Path $ua) {
    $uaCount = (Get-ChildItem $ua -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem "$($_.PSPath)\Count" -ErrorAction SilentlyContinue }).Count
}
$artefacts += [pscustomobject]@{
    Artefact = 'UserAssist (app launch counts / last run)'; Present = (Test-Path $ua)
    Detail = "$uaCount GUID subkeys"
}

$artefacts += [pscustomobject]@{
    Artefact = 'System uptime'; Present = $true
    Detail = "booted $((Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToString('yyyy-MM-dd HH:mm'))"
}

$prefetch = "$env:SystemRoot\Prefetch"
$pfCount = if (Test-Path $prefetch) { (Get-ChildItem $prefetch -Filter *.pf -ErrorAction SilentlyContinue).Count } else { 0 }
$artefacts += [pscustomobject]@{
    Artefact = 'Prefetch'; Present = ($pfCount -gt 0); Detail = "$pfCount .pf files"
}

$artefacts | Format-Table Artefact, Present, Detail -AutoSize

# Audit policy (needs elevation)
Write-Host ""
if ($isElevated) {
    Write-Host "Audit policy - Logon/Logoff category:" -ForegroundColor Cyan
    & auditpol /get /category:"Logon/Logoff" 2>$null | Where-Object { $_ -match '\S' }
} else {
    Write-Host "Audit policy: skipped (needs elevation)." -ForegroundColor Yellow
}

# Intune / Policy CSP ownership of audit settings
$csp = 'HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Audit'
Write-Host ""
if (Test-Path $csp) {
    Write-Host "Policy CSP owns audit settings (MDM-managed):" -ForegroundColor Yellow
    Get-ItemProperty $csp | Format-List
} else {
    Write-Host "No Policy CSP audit node - audit policy is not MDM-managed." -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# E. Verdict
# ---------------------------------------------------------------------------
Write-Section "E. Verdict"

$viableEdges = $density | Where-Object { $_.Kind -eq 'edge' -and $_.Status -eq 'VIABLE' }
$viablePres  = $density | Where-Object { $_.Kind -eq 'presence' -and $_.Status -eq 'VIABLE' }

Write-Host ("Viable edge signals     : {0}" -f $viableEdges.Count)
$viableEdges | ForEach-Object { Write-Host ("   + {0}" -f $_.Signal) -ForegroundColor Green }
Write-Host ("Viable presence signals : {0}" -f $viablePres.Count)
$viablePres  | ForEach-Object { Write-Host ("   + {0}" -f $_.Signal) -ForegroundColor Green }

$noAccess = $density | Where-Object { $_.Status -eq 'no access' }
if ($noAccess) {
    Write-Host ""
    Write-Host "Blocked by permissions (re-run elevated):" -ForegroundColor Yellow
    $noAccess | ForEach-Object { Write-Host ("   - {0}" -f $_.Signal) }
}

Write-Host ""
Write-Host "CSV output: $OutputDir" -ForegroundColor Cyan
Get-ChildItem $OutputDir -Filter *.csv | ForEach-Object { Write-Host ("   {0}" -f $_.FullName) }
