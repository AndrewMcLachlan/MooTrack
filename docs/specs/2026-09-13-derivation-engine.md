# Derivation engine — design

Turns raw capture events into defensible daily hours. First of three
deliverables (engine, agent, collector); built first because it is the one
piece testable today, against `docs/data/daily-hours.csv`.

Doubles as the statement of method: how a figure in the workbook was derived.

## Scope

In: events (or a sleepstudy report) → daily and weekly hours, with quality flags.
Out: capture (agent), transport and storage (collector).

## Structure

```
src/MooTrack.Derivation/    class library, net10.0 — pure, no I/O
src/MooTrack.Cli/           NDJSON or sleepstudy in, CSV/XLSX out (ClosedXML)
tests/MooTrack.Derivation.Tests/
```

The library reads no files, no clock, no config. The CLI does all I/O. The
collector will call the library directly, so derivation is written once.

## Model

An interval of working time **ends at the earliest evidence of departure** and
**starts at the earliest confirmed evidence of return**.

Departure evidence: `Lock`, `UserInactive`, `DisplayOff`.
Return evidence: `Unlock`, `UserPresent`, `DisplayOn`.

Asymmetric on purpose. Ending early is conservative. Starting early is
justified because time spent at the desk waiting for a machine to become usable
— boot, login, warm-up — is working time.

**Confirmed return.** A return anchors at the earliest return signal only if
user presence or an unlock follows within `ConfirmationWindow`. An unconfirmed
display-on is a spurious wake and contributes nothing. Without this, a
maintenance wake at 3am opens a working interval.

**Bridging.** Gaps shorter than `BridgeThreshold` are absorbed into the working
block — reading, thinking, a phone call. An explicit `Lock` is a hard boundary
and is never bridged, at any duration: inferred departures get the benefit of
the doubt, a deliberate Win+L does not.

**Day attribution.** Sessions split at local midnight. Daily granularity is the
tax requirement, so time lands in the day it happened.

**Fringe sessions.** An isolated session of at most `FringeMaxDuration`
separated by at least `FringeGap` from the rest of the day is counted in
totals but excluded when deriving start and finish. Unchanged from
`Extract-SleepStudyHours.py`.

## Pipeline

Each stage a pure function, separately tested.

| # | Stage | In → Out |
|---|---|---|
| 1 | Ingest | NDJSON → `Observation[]` |
| 2 | Project | observations → one `Interval[]` per signal |
| 3 | Coverage | tick stream → covered intervals; complement is recorder-gap |
| 4 | Algebra | `billed = displayOn − ⋃away`, with confirmed return |
| 5 | Bridge | merge gaps below threshold |
| 6 | Split | cut at local midnight |
| 7 | Trim | fringe rule, start/finish only |
| 8 | Report | `DayRecord` |

The sleepstudy adapter emits a display-on interval set and enters at stage 4,
so fixture and live data traverse identical algebra.

## Gaps and quality

Absence of ticks is positive evidence the recorder was down, and is the only
thing distinguishing downtime from a break. Coverage is derived from `Tick`
observations **alone** — a quiet period between real events is not evidence of an
outage, and treating it as one discards real hours. A tick run missing for longer
than `TickInterval + GapTolerance` becomes an explicit gap: never billed.

Coverage is judged against the **working day**, not the calendar day. A recorder
idle overnight says nothing about whether the day's hours are evidenced, so a gap
falling entirely outside the day's first-to-last span contributes nothing. A gap
that opens at or before the day's last activity counts to its full length,
including past the span: it leaves the end of that day unevidenced, and where the
day truly ended is exactly what is then unknown.

| Flag | Meaning |
|---|---|
| `Complete` | no gap intrudes on the working day |
| `Partial` | gaps present, quantified in `UnaccountedMinutes` |
| `Unreliable` | unaccounted time exceeds a third of the day's span |

A day with no activity but an overlapping gap is still reported, with zero hours
and `Unreliable`. A day the recorder missed must not silently vanish from a tax
record.

The engine never interpolates. Ambiguous input produces a flagged day, never a
guessed number.

## Durations

Every duration derives from the monotonic counter (`unbiasedMs`), cross-checked
against wall clock, never from wall clock alone — 81 time-change events were
observed in a fortnight. Where the two disagree beyond tolerance the interval is
flagged, not silently corrected.

Per gotcha 5, no gap-derived attribution may exceed elapsed wall clock.

## Parameters

Query-time, so hours recompute without re-recording. Every output records the
set that produced it.

| Name | Default |
|---|---|
| `BridgeThreshold` | 10 min |
| `ConfirmationWindow` | 30 min |
| `FringeMaxDuration` | 5 min |
| `FringeGap` | 90 min |
| `TickInterval` | 60 s |
| `GapTolerance` | 5 min |
| `BridgeAcrossLock` | false |

## Acceptance

Run display-only over `docs/data/raw/sleepstudy28.html`: must reproduce
`docs/data/daily-hours.csv` to two decimals, except on midnight-crossing days,
where the split rule diverges by design. This is the regression guard on the
interval maths.

The full multi-signal model is then compared to that baseline as a **measured
delta, not a match**. That delta is the break time the display-only method
over-billed, and reporting it is itself useful.

Per the brief: if the model disagrees with the CSV, the model is wrong. Do not
edit the CSV.

## Output

Daily CSV, weekly rollup, and a three-sheet Excel workbook via ClosedXML (Daily,
Weekly, Method). Each is written to a temporary file and atomically replaced; a
workbook held open by Excel fails with a message naming the file rather than
truncating it.

    date,weekday,first_active,last_active,span_hours,active_hours,away_hours,
    sessions,longest_break_min,fringe_sessions,quality,unaccounted_min

    iso_week,week_start,active_hours,days_with_activity,mean_hours_per_day,
    total_span_hours,partial_days,unreliable_days

Times are written at second precision. The validation CSV is minute-resolution,
so the acceptance comparison truncates.

The Method sheet records the parameter set that produced the figures.

## CLI

    mootrack --sleepstudy <report.html> --out <dir> [options]
    mootrack --ndjson <file-or-dir> --out <dir> [options]

`--offset`, `--bridge`, `--confirm`, `--fringe-max`, `--fringe-gap`,
`--gap-tolerance` and `--bridge-across-lock` override the defaults above.

## Not doing

Storage, ad-hoc querying, back-filling gaps from event logs, computing hours at
capture time. Raw is kept forever; hours are derived.

## Resolves

Repo convention open item: specs live in `docs/specs/`.
