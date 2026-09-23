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
7. Upstream security advisory: `Microsoft.OpenApi` 2.4.1 (GHSA-v5pm-xwqc-g5wc, high) pulled in
   transitively by the server.
