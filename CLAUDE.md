# Working in this repository

Read `README.md` first for what MooTrack is and how the model works. This file is
what you need to not break it.

## What the code is for

The output is used to substantiate a tax deduction. Two consequences:

- **Over-billing is worse than under-billing.** Where a judgement is genuinely
  ambiguous, the conservative reading wins.
- **A wrong number is worse than a missing one.** A flagged or absent day can be
  explained. A confidently wrong figure cannot.

Nothing is computed at capture time. The raw record is kept; hours are derived.

## Invariants

Breaking any of these is a defect even if every test passes.

- **The journal is the commit point.** An observation reaching the journal is the
  success condition. Shipping, deriving and reporting are downstream and may fail
  without costing the record.
- **Nothing corroborating is worth an observation.** A clock that cannot be read,
  a collector that cannot be reached, a source that will not subscribe — none of
  these justify discarding what was observed. Catch narrowly, at the point of the
  optional work, not around the write.
- **Derivation lives in the engine, never in the agent.** The agent records what
  it saw and nothing more.
- **Thresholds are query-time parameters.** Hours must be recomputable under
  different assumptions without re-recording.
- **Never attribute dormant time to active time.** Bound any gap-derived
  attribution by elapsed wall clock and discard what exceeds it.
- **Raw files are append-only.** One per local day, flushed per write, never
  rewritten.

## Domain gotchas

These cost real time to find. They are not obvious from the code.

1. **Duration units differ between reports that use the same field name.** Sleep
   study JSON is microseconds; sleep study XML and battery report XML are 100 ns
   ticks. Never share a constant between them.
2. **Sleep study JSON local timestamps carry a bogus trailing `Z`.** They are
   local, not UTC. Parse them naive. The XML variant does not do this.
3. **Active periods are split across two XML element types** —
   `RecentUsageInstance` and `OsStateInstance`, both `Type="Active"`. Neither is
   complete alone; take both and merge overlapping intervals.
4. **Clock changes are frequent** — dozens in a fortnight. Any duration computed
   from wall clock alone will eventually be wrong.
5. **Power setting GUIDs are session-scoped or session-0, and not
   interchangeable.** A service must use `GUID_GLOBAL_USER_PRESENCE`; the
   session-scoped equivalent is rejected with `ERROR_INVALID_PARAMETER`, which
   presents as a service that starts cleanly and observes nothing.
6. **The agent's own lifecycle is not user activity.** `AgentStarted` and
   `AgentStopped` are deliberately absent from the departure and return sets;
   treating a restart as a return ends a break early and bills time not worked.
7. **A misconfigured mount looks exactly like a working one.** A bind mount that
   fails to attach leaves a writable directory in its place; one the container
   cannot write to still passes a health check. Both are proved at startup and
   both refuse to start, because neither is visible from inside afterwards. No
   `VOLUME` directives either — they shadow bind mounts.
8. **Fringe sessions distort start and finish.** An isolated sub-five-minute
   session hours after the rest moves a day's finish by hours. Count them in
   totals; exclude them when deriving start and finish.

## Conventions

Enforced by `.editorconfig` with `EnforceCodeStyleInBuild`, so the build catches
violations rather than only an editor.

- Private instance fields are `_camelCase`. Static fields are `PascalCase`.
- Accessibility modifiers are explicit on every non-interface member.
- Language keywords for declarations (`string name`), framework names for static
  member access (`String.IsNullOrEmpty`).
- Builds are warning-free. A warning is a defect.

Comments explain constraints that would fail silently if broken, and nothing else.
Do not write a comment justifying something's absence, narrating what the code
plainly does, or recording what a previous version did.

## Testing

- Test first, and watch the test fail before implementing. A test that has never
  failed has not been shown to test anything.
- When a test passes on its first run, mutate the code it covers and confirm it
  fails. Otherwise it is decoration.
- Prefer real behaviour over mocks. The Win32 layer is confined to `Native.cs` and
  `DesktopMonitor.cs` precisely so everything else is testable anywhere.
- **Tests run on Linux in CI** while the agent targets Windows. Platform-specific
  calls must degrade, not throw, or the suite fails for reasons unrelated to the
  change.
- Verification means running the command and reading the output. "Should work" is
  not a result.

## The acceptance test

The derivation engine, run display-only over a sleep study report, must reproduce
the recorded daily figures exactly. **If the model disagrees with the recorded
data, the model is wrong** — do not adjust the data to match.

That data is not in the repository. Those tests are skipped, not failed, when it
is absent. `MOOTRACK_VALIDATION_DATA` points them at a directory holding it.

## Commands

```
dotnet build MooTrack.slnx
dotnet test MooTrack.slnx
dotnet run --project src/MooTrack.Cli -- --ndjson <journal-dir> --out <out-dir>
```
