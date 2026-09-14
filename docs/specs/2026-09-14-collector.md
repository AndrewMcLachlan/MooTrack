# Collector — design

Receives observations from the agent, stores them durably, and regenerates the
derived reports. Third of three deliverables, after the
[derivation engine](2026-09-13-derivation-engine.md) and the
[agent](2026-09-13-agent.md).

## Why an API at all

LocalSystem cannot authenticate to the Synology — it presents the machine
account, which means nothing to the NAS. An outbound POST with an API key header
sidesteps SMB and stored credentials entirely.

## Shape

```
src/MooTrack.Reporting/   CSV and workbook writing, shared with the CLI
src/MooTrack.Collector/   ASP.NET Core minimal API, net10.0
tests/MooTrack.Collector.Tests/
deploy/docker-compose.yml
```

`MooTrack.Reporting` exists so the CLI and the collector cannot drift: one
implementation of the daily CSV, the weekly rollup and the workbook.

## Endpoints

| Method | Path | Auth | Purpose |
|---|---|---|---|
| POST | `/observations` | `X-Api-Key` | Ingest an NDJSON batch |
| POST | `/regenerate` | `X-Api-Key` | Force a report rebuild |
| GET | `/health` | open | Liveness and mount evidence |

The key is compared with a fixed-time comparison, and the collector **refuses to
start** without one rather than running unauthenticated.

Ingest returns `{accepted, duplicates, malformed}`. A malformed line never fails
the batch: the rest is stored and the count reported, because discarding good
observations over one bad line loses record that cannot be recreated.

## Storage

Append-only NDJSON, one file per host per local day, under `RawRoot`. Flushed to
disk per write, never rewritten.

Dedupe is on `(host, event, timestamp-to-second)` — computed by
the collector from the parsed observation rather than trusting the agent's
`dedupeKey`. Keys for a day are loaded from disk on first touch, so idempotency
survives a restart. This is what makes the agent's retry-until-accepted shipping
safe: a batch that is delivered but whose response is lost can be sent again
without duplicating anything.

## The mount check

A bind mount that fails to attach is not an error. Docker leaves the container's own
directory in its place — writable, empty, and indistinguishable from the real thing.
The collector would run for weeks and lose everything the next time the container
was recreated.

So at startup, when `RequireMountedRawRoot` is set, `RawRoot` is checked against
`/proc/self/mountinfo` and the collector **refuses to start** if it is not a mount
point. The image sets that flag, so the check is on exactly where it matters and off
for local runs and tests.

That replaced an earlier design that mirrored every write to a container-local
volume. Duplicating data in a second place is a worse answer than not making the
mistake: the copy costs storage forever, is never read, and still leaves a
misconfigured deployment running.

A directory the container cannot write to fails the same way for the same reason:
the service starts, `/health` returns ok, and every ingest fails. So writability is
proved at startup too, by writing and deleting a file, and the collector refuses to
start if `RawRoot` fails. The message names the uid and the `chown` that fixes it,
because the cause is never visible from inside the container.

`ReportRoot` is checked but is not fatal. Reports are derived and can be rebuilt;
refusing observations that are still arriving would be the wrong trade.

Per gotcha 7, the image declares no `VOLUME` directives, which would shadow the bind
mounts.

A failed write fails the request, so the agent keeps the batch and retries.

## Derivation

Runs here, never in the agent. Reports are rebuilt from the whole raw store on
ingest, debounced (default 60 s) so a burst of batches costs one rebuild.
Regeneration failure is logged and leaves the raw store untouched — raw is the
record, reports are derived.

`GET /hours` recomputes on demand under thresholds supplied per request —
`bridge`, `confirm`, `gap`, and a `from`/`to` range — and answers without
touching the stored reports. This is what is meant by the idle threshold
being a query-time parameter: the same raw record can be read under different
assumptions, and the answer carries the parameters that produced it.

The CSVs are written before the workbook and the workbook is best-effort. It can
be held open by a spreadsheet, and ClosedXML measures columns through
SixLabors.Fonts, which needs font files present. Neither is a reason to lose a
report, so a workbook failure is logged and the CSVs stand.

## Configuration

Environment variables. `MOOTRACK_API_KEY` for the key; everything else as
`MooTrack__<Key>`.

| Key | Default |
|---|---|
| `RawRoot` | `/data/raw` |
| `RequireMountedRawRoot` | false; the image sets it true |
| `ReportRoot` | `/data/reports` |
| `RegenerateDebounceSeconds` | 60 |
| `BridgeMinutes` | 10 |
| `ConfirmationMinutes` | 30 |
| `GapToleranceMinutes` | 5 |

## Image

Runtime-only Alpine image, `linux/amd64` for the DS918+, published to
`ghcr.io/andrewmclachlan/mootrack-collector`. Runs as the non-root `$APP_UID`.
`fontconfig` and `ttf-dejavu` are installed because ClosedXML measures column
widths through SixLabors.Fonts, which needs font files on disk.

**The Dockerfile packages, it does not build.** Its context is a published output
directory:

    dotnet publish src/MooTrack.Collector/MooTrack.Collector.csproj -c Release -o artifacts/collector
    docker build -f src/MooTrack.Collector/Dockerfile -t mootrack-collector artifacts/collector

A multi-stage build would recompile inside the image, so the bits that shipped
would not be the bits the test suite ran against. Publishing once and packaging
that output keeps them the same artefact. It also means the image needs no SDK
layer and no source.

`.github/workflows/collector-image.yml` tests, publishes, then packages, on push
to `main` or manual dispatch.

## Verified

The real agent was run against the real collector over HTTP. Observations shipped
with `X-Api-Key`, raw NDJSON landed under the host directory, reports regenerated
automatically, and re-posting an identical batch returned
`{"accepted":0,"duplicates":9}`.

The image was built and run both ways: with the volumes mounted it serves, ingests
and derives a posted day to 7.50 active hours against 8.50 span, writing a valid
workbook inside the container; with the volumes omitted it logs the path and exits
rather than starting. It runs as uid 1654, and `Config.Volumes` is null so no
`VOLUME` shadows a bind mount.

## Not doing

Per-observation acknowledgement, a database, or authentication beyond the shared
key. One machine, one user, one key, on a LAN.
