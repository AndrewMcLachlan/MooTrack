# MooTrack

Records working hours automatically, with no start/stop button, for tax deduction
substantiation. That purpose sets the bar: **daily** granularity, defensible start
and finish times, and break accounting. Weekly aggregates are not sufficient.

## The idea

On a machine used only for work, **screen-on time is the working-hours signal.**
A day reconstructed from screen-on sessions is very different from one billed
first-login to last-logoff — the gap between the two runs to hours per day, and
that gap is what a naive lock/unlock design would wrongly claim.

Nothing is computed at capture time. The agent records what it observed; hours are
derived later, from the raw record, under parameters that can be changed without
re-recording anything.

## Components

```
MooTrack.Derivation    observations in, daily and weekly hours out. Pure, no I/O.
MooTrack.Agent         Windows service. Observes, journals, ships.
MooTrack.Collector     ASP.NET Core API. Receives, stores, derives.
MooTrack.Reporting     CSV and Excel writing, shared by the CLI and the collector.
MooTrack.Cli           derive hours from a journal or a sleep study report.
```

The agent writes an append-only NDJSON journal, one file per local day, flushed per
line. **That journal is the commit point.** Posting to the collector is replication
and may fail indefinitely without touching the record.

## The model

An interval of working time **ends at the earliest evidence of departure** and
**starts at the earliest confirmed evidence of return.**

| | |
|---|---|
| Departure | `Lock`, `UserInactive`, `DisplayOff` |
| Return | `Unlock`, `UserPresent`, `DisplayOn` |

Asymmetric deliberately. Ending early is conservative. Starting early is justified
because time spent at the desk waiting for a machine to become usable — boot,
login, warm-up — is working time.

A return only counts if user presence or an unlock confirms it within a window, so
a maintenance wake in the small hours contributes nothing.

Gaps shorter than the bridge threshold are absorbed into the working block: they
are reading, thinking, a phone call. An explicit lock is a hard boundary and is
never bridged — inferred departures get the benefit of the doubt, a deliberate
Win+L does not.

Every derived figure carries the parameters that produced it.

## Evidence and quality

Absence of the 60-second tick is positive evidence the recorder was down, and is
the only thing distinguishing downtime from a break. Coverage is judged against the
working day rather than the calendar day — a recorder idle overnight says nothing
about whether that day's hours are evidenced.

Each day is reported `Complete`, `Partial` or `Unreliable`, with the unaccounted
minutes quantified. A day the recorder missed is still reported, with zero hours
and a flag. **The engine never interpolates.** Ambiguous input produces a flagged
day, never a guessed number.

## Building

```
dotnet build MooTrack.slnx
dotnet test MooTrack.slnx
```

The collector image is built by CI and published to
`ghcr.io/andrewmclachlan/mootrack-collector`. The Dockerfile packages a published
output rather than building from source, so what ships is what the suite tested.

```
dotnet publish src/MooTrack.Collector/MooTrack.Collector.csproj -c Release -o artifacts/collector
docker build -f src/MooTrack.Collector/Dockerfile -t mootrack-collector artifacts/collector
```

## Deploying

`deploy/` holds a compose file for the collector and a standalone folder for the
agent — an installer, an uninstaller, a health check and an hours report, for
machines where policy blocks Windows Installer. `installer/` builds an MSI for
machines where it does not.

Configuration for the agent lives under `%ProgramData%`, not beside the binaries,
so reinstalling cannot discard the collector URL and API key. The journal is
likewise never removed by an uninstall: it is the substantiation record and
outlives the software that wrote it.

## Recorded hours

`docs/data/` is not in this repository. It holds a real person's movements at
minute resolution, which is not something to publish. The acceptance tests that
derive against it are skipped, not failed, when it is absent, so a clean checkout
builds green. See `docs/data/README.md`.

## Design notes

`docs/specs/` carries a design document per component, including the reasoning
behind decisions that look arbitrary until something breaks. `docs/tools/` holds
the Python and PowerShell used to survey the available signals and to establish
the derivation model before it was implemented in .NET.
