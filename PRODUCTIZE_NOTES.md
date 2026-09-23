# Iron Clad Support — Productization Notes

Fork of `0x101-Cyber-Security/NetLock-RMM` (C#/.NET 10). Working branch: `productize/iron-clad-support`.
Repo visibility checked 2026-09-23: **PUBLIC** fork → normal PR/CI flow allowed.

> Scope-tier note: this repo is not listed in the global ICIT tier table (IN SCOPE / REVIEW ONLY /
> HANDS OFF). The repo's own `CLAUDE.md` (committed by Bill) authorizes work on
> `productize/iron-clad-support`. Until Bill assigns a tier it is treated as **REVIEW ONLY**:
> branch + PR, never merge, never deploy. The ICIT Python/`module_framework`/Firestore architecture
> does not apply to this .NET RMM codebase.

## Environment facts

- `NetLock-RMM-Server` does **not** compile from the public upstream source: the closed-source
  `Members_Portal` namespace and `MySQL.Handler.Operator_Info` / `Resolve_Operator` are stripped
  (`//OSSCH_START ... //OSSCH_END` markers). 4 pre-existing errors (CS0234 x2, CS0426 x2).
  Consequence: server code cannot be unit-tested by project reference. Testable logic is kept in
  dependency-free files and compile-linked into `tests/IronClad.Server.Tests`.
- The MySQL schema is not in the repo (installed out of band). Tests create the subset they need.
- DB tests use a real MySQL/MariaDB (`ICS_TEST_MYSQL` connection string; defaults to the local
  unix socket as root) and create/drop a throwaway `iclad_test_*` database per test.

## Fix 1 — Lost notifications for events created during a notification run (priority 1)

**Failure.** An agent/uptime event inserted while `Events_Notification_Service` was mid-run was
marked as sent on every channel without any notification being delivered.

**Root cause.** `ProcessEventsTask` ran one `SELECT ... WHERE <channel>_status = 0` per channel
sequentially (each channel may block on SMTP/HTTP), then `Sender.Mark_Old_Read` executed
`UPDATE events SET <all channels> = 1 WHERE date < finished_time`. Any event inserted after a
channel's SELECT but dated before `finished_time` was flagged as delivered for that channel.

**Fix.** Snapshot `MAX(id)` at the start of the run (the watermark). Both the per-channel SELECT
and the final mark UPDATE are bounded by `id <= @watermark` (parameterised). Events created
mid-run are untouched and delivered on the next run. Channel status columns are now looked up in a
whitelist (`Notification_Batch.Channels`) instead of being string-interpolated unchecked.
Files: `NetLock-RMM-Server/Events/Notification_Batch.cs` (new),
`NetLock-RMM-Server/Events/Sender.cs`, `NetLock-RMM-Server/Background_Services/Events_Notification_Service.cs`.
Upstream semantics otherwise preserved (one delivery attempt per run; events are still marked
processed after the run even if a channel send failed).

**Validation.**
- `dotnet test tests/IronClad.Server.Tests` → 9/9 passed against MariaDB 10.11.
- `Legacy_date_based_marking_drops_event_inserted_mid_run` reproduces the defect with the old SQL.
- `Watermark_run_leaves_event_inserted_mid_run_for_next_run` proves the fix: event 2 is left
  pending after run 1 and delivered on all 5 channels in run 2.
- Mutation check: making the mark UPDATE unbounded (`OR 1=1`) fails the regression test.
- Server build: error set unchanged (only the 4 pre-existing upstream errors); no new errors.
- Not validated: an end-to-end run of the real server (it cannot be built from public source).

## Fix 2 — Sensors deleted / never firing because of scheduler timestamp handling (priority 2)

The agent project does not compile from public source either (`Global.Encryption.String_Encryption`
is stripped; 39 pre-existing errors). Helper logic lives in the dependency-free
`NetLock RMM Agent Comm/Global/Sensors/Schedule_Time.cs`, which is compile-linked into
`tests/IronClad.Agent.Tests` (net8.0, ICU cultures enabled).

**Failure A: sensors deleted on non-US agents.** After a sensor's first execution, `last_run` is
written with `InvariantCulture` (`MM/dd/yyyy HH:mm:ss`). Schedule types 0/1/2/5/6/7 re-read it with
`DateTime.Parse(value)`, which uses the machine culture. On de-DE and similar locales that throws for
days > 12. The per-sensor `catch` then **deletes the sensor file**, so monitoring silently stops
until the next policy sync. For days ≤ 12, day and month are silently swapped, which causes wrong
scheduling.
*Root cause:* mixed culture-sensitive and invariant writes and parses of the same field.
*Fix:* all reads and writes go through `Schedule_Time` (one invariant format). A stored value is
normalized once per check: legacy values written with the machine culture are still accepted, and
unreadable values become "never run" instead of throwing.

**Failure B: "date & time" (type 1) sensors fail on every check.** The agent parses
`time_scheduler_date` with the exact format `dd.MM.yyyy`, but the web console stores `yyyy-MM-dd`
(`Add_Sensor_Dialog.razor`, `Edit_Sensor_Dialog.razor`). The result is a FormatException and the
sensor file is deleted.
*Fix:* `Schedule_Time.Parse_Schedule_Date` accepts `yyyy-MM-dd` and legacy `dd.MM.yyyy`. Types 5–7
use it too.

**Failure C: event-log sensors (category 1) could not match real events.** The EventLog XPath window
was `[startTime, endTime]`, and both values were captured at the start of execution, so the window
was microseconds wide. The in-loop check also compared events against `last_run`, which had already
been overwritten with the current start time.
*Fix:* the window is `[previous run, this run)` (`Schedule_Time.Since_Previous_Run`), truncated to the
stored precision so consecutive runs tile without gap or overlap. Without a usable previous run the
window is empty, so the full log history is never replayed.

**Validation.**
- `dotnet test tests/IronClad.Agent.Tests` → 33/33 passed across de-DE, en-GB, en-US, fr-FR, ja-JP.
- Legacy-reproduction tests (`Legacy_Behavior_Reproduction`) confirm the upstream throw, the
  day/month swap, and the date-format mismatch.
- Agent build: error set unchanged versus baseline. With a temporary `String_Encryption` stub the
  whole agent compiles with **0 errors**; the stub was removed and not committed.
- Not validated: a live Windows EventLog query (no Windows host here). The XPath uses `>=` / `<`
  on `@SystemTime` with ISO-8601 UTC timestamps, the same form upstream used.

## Fix 3 — Job and Defender scan-job schedulers: same timestamp defects (priority 3)

**Failure.** `Global/Jobs/Time_Scheduler.cs` had the same bug as sensors: `last_run` written in
InvariantCulture, then read with the machine culture, and type 1 dates parsed as `dd.MM.yyyy`. A
failing job is deleted by the per-job catch. `Scan_Jobs_Scheduler.cs` wrote and read `last_run` in
the machine culture consistently, but its "date & time", "following days at X time" and "following
days, x hours" types parsed `time_scheduler_date` as `dd.MM.yyyy`. The scan-job dialogs store
`DateTime.ToString()` in the console's request culture, so on an en-US console
(`9/23/2026 2:30:00 PM`) every such scan job threw. The scan-job loop has no per-job `try`, so one
bad job also aborted every job after it on each cycle.
*Fix:* both schedulers use `Schedule_Time` for every `last_run` read/write and schedule-date parse.
`Parse_Schedule_Date` also accepts `M/d/yyyy`. The console only produces yyyy-MM-dd, de-DE
dd.MM.yyyy or en-US M/d/yyyy, so slash dates are unambiguous.
**Validation:** 38/38 agent tests, including a reproduction of the en-US scan-job failure. The full
agent compiles with 0 errors when the temporary `String_Encryption` stub is present (stub not
committed).

## Backlog — findings from reviewing the notification pipeline (not yet changed)

1. **No retry on transient send failure.** A failed SMTP/Teams/etc. send is dropped once the run
   marks the event processed. Needs a bounded retry (attempt counter column → schema change).
2. **No deduplication/suppression.** A flapping sensor or connect/disconnect loop sends one
   notification per event. Candidate: suppress identical (device_id, reported_by, _event) within a
   window; requires a design decision on window and on "resolved" notifications.
3. **Severity is exact-match** (`event.severity == rule.severity || rule.severity == any`). A rule
   set to "high" does not fire for "critical". Likely intended as a threshold by operators; product
   decision needed before changing (UI labels: low/moderate/high/critical/any = 0..4).
4. **`read = 0` filter**: an operator viewing an event in the console before the run suppresses its
   notification. Confirm intended.
5. **Partial-recipient success**: the event is marked sent for a channel if *any* recipient row
   succeeded; other failed recipients are not retried.
6. Remaining string-concatenated SQL in `Sender.cs` (`WHERE id = " + id`) — values come from the
   DB (int PK), low risk, but should be parameterised when touched.
7. Scan-job loop (`Scan_Jobs_Scheduler.Check_Execution`) has no per-job try/catch: one malformed job
   aborts the remaining jobs every cycle. Needs a re-indent of ~300 upstream lines; deferred to
   keep upstream merges clean.
8. Upstream security advisory: `Microsoft.OpenApi` 2.4.1 (GHSA-v5pm-xwqc-g5wc, high) pulled in
   transitively by the server.
