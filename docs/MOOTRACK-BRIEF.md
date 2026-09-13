# MooTrack — working hours capture

Handover brief. Everything below was established empirically on the target
machine, not assumed. Where something is inference rather than measurement it
says so.

## Purpose

Capture working hours automatically, with no start/stop button, and
record them for **tax deduction substantiation**. That requires **daily**
granularity with defensible start/finish times and break accounting. Weekly
aggregates are not sufficient.

## Environment (verified)

| Fact | Value |
|---|---|
| Machine | `WORKSTATION` |
| OS | Windows 11, build 26100.9106 |
| Join state | Entra-joined only. No domain GPO applied (local GPO only) |
| Audit policy control | **Not** MDM-managed — no `PolicyManager\current\device\Audit` node |
| Rights | Local admin available. No domain admin. |
| Sleep model | **Modern Standby (S0 Low Power Idle)**. S1/S2/S3 unavailable. Hibernate + Fast Startup enabled |
| Power | Effectively always on AC — `ActiveDcTime` 0.0 across a full fortnight |
| Uptime pattern | Long uptimes (23+ days observed). Reboots rare; standby is the normal transition |
| Sense service | Check before shipping — Defender for Endpoint status not yet confirmed |

## Signal survey results

Run `Survey-WorkTimeSignals.ps1` to reproduce. Key outcomes:

**Viable**
- `Security` 4624 / 4634 — but ~190/day, almost all service/network noise. Filter to logon types 2, 7, 11.
- `Security` 4800 / 4801 — **now enabled and confirmed firing.** Was off; `auditpol /set /subcategory:"Other Logon/Logoff Events" /success:enable` applied and persists (nothing manages it).
- `Microsoft-Windows-Winlogon/Operational` — 53/day, densest edge source, no elevation needed. ID breakdown not yet characterised.
- `Microsoft-Windows-NetworkProfile/Operational` 10000/10001 — good liveness proxy, fires on wake.
- `Microsoft-Windows-Shell-Core/Operational` — presence signal.
- `System` 6005/6006/6013.

**Empty, and why**
- Kernel-Power 42/107 — Modern Standby uses **506/507** instead. The 6 observed 42/107 events were true hibernations.
- Kernel-General 12/13, User32 1074, Kernel-Power 41 — the machine simply doesn't reboot or crash.
- 4688 — process auditing not enabled. Leave it that way; volume would destroy the retention window.

**Retention**
- `Security` was 20 MB ≈ 8.9 days. Raised to 500 MB (`wevtutil sl Security /ms:524288000`).
- `TerminalServices-LocalSessionManager` 1 MB ≈ 3.1 days — raise if its IDs prove useful.

## The core finding

**Screen-on time is the working-hours signal.** This machine is work-only; if
it's in use, it's work. Screen-on sessions reconstruct the day at session
granularity, and the gap between screen-on total and first-to-last span runs to
hours per day — that gap is what a naive lock/unlock design would wrongly bill.

Baseline from 19 extracted weekdays (17/08–11/09/2026):
a stable weekday median with a few hours of day-to-day variation, independently
consistent with a two-year battery-report series. The figures themselves live with
the data, not here.

### Live capture mechanism

`RegisterPowerSettingNotification` from the service:

- **`GUID_SESSION_USER_PRESENT`** — user present vs inactive. Modern Standby-aware
  replacement for `GetLastInputInfo`, and **delivered to the service**, so the
  session 0 isolation problem does not apply.
- **`GUID_CONSOLE_DISPLAY_STATE`** — display on/off/dimmed. Produces the same
  boundaries the sleep study records.

This supersedes an earlier plan to use `WTSQuerySessionInformation`. Do not
reintroduce it.

Supporting signals (corroboration, not dependencies):
- `OnSessionChange` (`SERVICE_CONTROL_SESSIONCHANGE`) — lock/unlock/logon/logoff, needs no audit policy.
- `OnPowerEvent` — suspend/resume.
- `OnShutdown` — exact timestamp on clean shutdown/restart. Best-effort only.
- Security 4800/4801 — independent check on idle-derived day start.

## Architecture

Two components.

### 1. Windows service (build first)

Runs as LocalSystem. Responsibilities:

- Subscribe to the power setting notifications above.
- Handle session change, power, shutdown callbacks.
- Watch `Security` and `System` via `EventLogWatcher` with a **persisted
  `EventBookmark`** — resume from bookmark on start so downtime replays.
- Append every observation to a **local append-only NDJSON file, one per day,
  flushed per write**. This is the durable store. Never rewrite a past file.
- Emit a periodic tick (60 s) carrying wall clock, UTC offset, and a monotonic
  counter (`QueryUnbiasedInterruptTime`).
- Spool and POST to the collector. Local file is written unconditionally,
  regardless of whether the post succeeds.

Record shape:

```json
{ "tsUtc": "...", "tsOffset": "+10:00", "unbiasedMs": 0,
  "host": "...", "user": "...", "event": "DisplayOn|DisplayOff|UserPresent|UserInactive|Lock|Unlock|Suspend|Resume|Shutdown|Tick|Gap",
  "source": "PowerNotify|SessionChange|EventLog|Tick|Reconcile",
  "dedupeKey": "sha256(host|user|event|tsUtc-to-second)",
  "detail": "" }
```

Unattended operation: register via Task Scheduler with triggers at logon, at
startup, on workstation unlock, **and repeat every 5 minutes indefinitely** with
"do not start a new instance". Task Scheduler is the watchdog; no supervisor
process to also fail.

### 2. Collector (build second)

Runs on the NAS (`the NAS`, Synology DS918+, amd64, Container Manager).

The API exists **because LocalSystem cannot authenticate to the Synology** — it
presents the machine account, which means nothing to the NAS. An outbound HTTP
POST with an API key header sidesteps SMB and stored credentials entirely.

- ASP.NET Core minimal API, .NET 10, single batch endpoint, `X-Api-Key` checked
  against an env var.
- Writes raw NDJSON to a bind-mounted share path.
- Maintains a **derived** day-by-day Excel workbook. Regenerate from raw each
  sync, write to temp, atomic replace. If locked by Excel, skip and retry.
  **ClosedXML** (MIT) — not EPPlus, whose licence is non-commercial only.
- Idempotent ingest: dedupe on `(host, event, timestamp-to-second)`.
- Derivation lives here, never in the agent. Idle threshold is a query-time
  parameter so hours can be recomputed without re-recording.

Also mirror writes to a container-local volume — a bind mount that silently
misses its target is otherwise invisible for weeks.

## Gotchas (all cost real time to find)

1. **Duration units differ between reports with the same field name.**
   Sleep study JSON = microseconds. Sleep study XML = 100 ns ticks.
   Battery report XML = 100 ns ticks. Never share a constant.
2. **Sleep study JSON local timestamps carry a bogus trailing `Z`.** They are
   local, not UTC. Parse naive. The XML variant does not do this.
3. **Active periods are split across two XML element types** —
   `RecentUsageInstance` and `OsStateInstance` (both `Type="Active"`). Neither is
   complete alone. Take both and merge overlapping intervals.
4. **Clock changes are frequent** — 81 time-change events in 14 days. Any
   duration computed from wall clock alone will be wrong. Record monotonic time
   alongside it, always.
5. **Never attribute dormant time to active time on resume.** Windows itself
   gets this wrong: after a 12-day absence the battery report reported 45 h/day
   at a 4.98 accounted/elapsed ratio. Bound any gap-derived attribution by
   elapsed wall clock and discard anything exceeding it.
6. **Fringe sessions distort start/finish.** An isolated sub-5-minute session
   hours after the rest moved one day's finish from 17:52 to 23:25. Count them
   in totals; exclude them when deriving start/finish.
7. **Docker `VOLUME` directives shadow bind mounts** when paths don't match
   exactly. Verify with `docker inspect` before trusting the first day of data.

## Open items

- Characterise `Winlogon/Operational` ID breakdown — may be a better edge source than Security.
- Confirm `Sense` (Defender for Endpoint) status before the agent ships.
- Decide break-handling policy: observed breaks run to 204 minutes. Whether
  those are deductible working time is a judgement the data can't make.
- Repo convention: agent plan/config location is currently inconsistent across
  repos (`.agents/plans`, `.claude/plans`, `.superpowers`, `docs/superpowers`).
  Pick one for this repo.

## Non-goals / rejected

- Start/stop button of any kind.
- `WTSQuerySessionInformation` for idle — superseded.
- Battery-derived signals — machine is always on AC.
- 4688 process auditing — retention cost far exceeds value.
- Weekly-only reporting — insufficient for the tax purpose.
- Computing hours at capture time. Raw is kept forever; hours are derived.

## Existing artefacts

Expected repo layout:

```
tools/
  Survey-WorkTimeSignals.ps1      signal survey. Known bugs: Test-Path on SRUM
                                  needs -ErrorAction SilentlyContinue; section C
                                  only maps Security/System IDs
  Extract-SleepStudyHours.py      daily/weekly hours from a sleepstudy HTML report
  Build-WeeklyHours.py            weekly series from a battery report XML
data/
  daily-hours.csv                 19 weekdays at session precision — VALIDATION SET
  weekly-hours.csv                two-year weekly series with quality flags
  weekly-hours-sleepstudy.csv     four-week rollup from the sleep study
data/raw/
  sleepstudy28.html               28-day sleep study (XML embedded)
  sleepstudy-report.json          14-day sleep study, JSON variant
  battery-report.xml              two years of weekly history
```

`data/daily-hours.csv` is the acceptance test. The service's first fortnight
must reproduce those daily figures. If it doesn't, the model is wrong — do not
adjust the CSV to match.

The raw reports are irreplaceable: the sleep study window has already rolled
past 17/08/2026, and the originals were written to `%TEMP%`. Commit them.
Going forward, archive fresh `powercfg /sleepstudy /duration 28` and
`/batteryreport` output periodically — weekly battery history survives for
years, the sleep study does not.
