# Import plan

How the import is put together and how it is tested. Rules are referenced by their entries in [decisions.md](decisions.md); requirements are in [ticket.md](ticket.md).

## Components

All in `transactions-ingest.Core/Import/`.

| Component | Responsibility | Rules |
|---|---|---|
| `RunClock` | One per run: `Now`, `Cutoff = Now - 24h`, `FutureLimit = Now + FutureTolerance`, `IsMutable(t) => t > Cutoff`. Built once from `TimeProvider`; every other component takes it. | D3 |
| `ITransactionSource` | `SourceSystem` + `FetchAsync(ct)` returning `RawTransaction`s (all fields strings, so parsing lives in one place). A failing source throws; it never returns an empty list instead. | D1, D4, D10 |
| `TransactionValidator` | Pure. `Validate(raw, sourceSystem, clock)` → `UnparseableKey`, `RejectedRecord(key, reasons)` or `ValidRecord(parsed)`. Reasons never contain card values. | D2 |
| `SnapshotParser` | Builds `ParsedSnapshot`: valid records by key, `PresentKeys` (every parseable key, rejected ones included), counts, and `RevocationAllowed` (false if any key is unparseable or duplicated). Records sharing a duplicate key are all rejected. | D4 |
| `ReconciliationPlanner` | Pure. `(snapshot, existing rows for the source, clock)` → `ReconciliationPlan`: inserts, updates (with field changes), reinstatements, revocations, `Ignored`, `RevocationCandidates`, `RevocationSkipped`. Compares normalized values (ordinal strings, decimal by value, `DateTime` by ticks). | D3, D4, D5 |
| `RevocationGuard` | Trips when enabled, candidates ≥ `MinCandidates`, and `revocations × 100 > MaxRevokedPercent × candidates`. Checked before anything is applied; a trip fails the source with no changes, inserts included. | D4 |
| `PlanApplier` | Applies the plan to tracked entities and writes `TransactionAudit` rows: Created, one Updated per field, Revoked, Reinstated. Revoke sets `RevokedAt`, reinstate clears it. | D3, D6 |
| `Finalizer` | Own context and transaction, after all sources and regardless of their outcome: `Active && TransactionTime <= Cutoff` → Finalized, `FinalizedAt`, Finalized audit. | D3 |
| `IImportLock` / `InMemoryImportLock` | Singleton `SemaphoreSlim`; `TryAcquire()` returns a releaser or null. | D8 |
| `ImportService` | `RunAsync(ct)` → `ImportResult` (AlreadyRunning or the run summary). Sequence below. | D4, D8 |
| `ImportOptions` | Bound from `Import`, validated on start: `FutureTolerance` (5 min), `RevocationGuard { Enabled = true, MinCandidates = 10, MaxRevokedPercent = 50 }`. The 24h window is a constant. | D2, D3, D4 |
| `AddTransactionsImport(config)` | DI registration; a scheduled job resolves `ImportService` and gets the lock for free. | D1, D8 |
| `ImportController` (Server) | `POST /api/import`: AlreadyRunning → 409, otherwise 200 + summary. | D8, D11 |

### ImportService sequence

1. Acquire the lock, or return AlreadyRunning.
2. Build the `RunClock`.
3. Save an `ImportRun` (Running); audits need its Id.
4. Per source:
   1. Fetch the snapshot outside any DB transaction.
   2. Parse, load existing rows, plan, check the guard.
   3. In a fresh context from `IDbContextFactory`: begin transaction, apply, add the `ImportRunSource` row, commit. A failed source's context is discarded, so nothing leaks into the next source.
5. Run the `Finalizer`.
6. Set the run to Succeeded / PartiallyFailed / Failed and `CompletedAt`.
7. `finally`: if the run is still Running, mark it Failed; release the lock.

`StartedAt` and every business timestamp (`CreatedAt`, `UpdatedAt`, `RevokedAt`, `FinalizedAt`, audit `ChangedAt`) use the run's clock. `CompletedAt` is run bookkeeping and reads the real time when the run ends.

Status changes (Created, Revoked, Reinstated, Finalized) are audit rows with no `FieldName` (D6); field changes are `Updated` rows with `FieldName`, `OldValue`, `NewValue`.

Logging: one warning per source with the ignored count (details at Debug), an error per failed source, guard trips and skipped revocations at error level. Card values are never logged.

### Counting

- `Ignored` counts only locked rows whose incoming data differs from what is stored. Unchanged rows count nothing, so a re-run reports all zeros (D5).
- `Rejected` counts records: validation failures, unparseable keys, and every record sharing a duplicate key.
- A reinstated row counts as `Reinstated`, not `Updated`, even when fields changed too.
- A Revoked row whose reappearing record fails validation stays Revoked and is not counted as reinstated (its key is present, so it is not revoked again either).

### Mock sources

JSON files per D10. JSON numbers are mapped to `RawTransaction` strings from the raw JSON text, never through `double`, so `10.10` and large IDs arrive exactly as written.

## Window queries

The cutoff is computed in C# and compared against the column; no date arithmetic on columns in SQL.

- Load existing rows for a source: `Where(t => t.SourceSystem == src && (t.TransactionTime > cutoff || snapshotKeys.Contains(t.TransactionId)))`
- Finalize: `Where(t => t.Status == Active && t.TransactionTime <= cutoff)`

`cutoff` goes through the same UTC converter as the column, so both sides are EF's TEXT format. Lexical order matches time order, including fractional seconds (trailing zeros are trimmed, so a shorter value is a prefix of the longer one). `IsMutable` in C# and the SQL filter use the same `>` against the same `Cutoff`, so they cannot disagree at the boundary. Boundary tests with fractional seconds confirm the SQL side.

## Test cases

### D2 — validation
- LocationCode / ProductName: 19 chars accepted, 20 rejected.
- LocationCode / ProductName empty, whitespace or missing → rejected.
- CardNumber accepted: missing or `""` (stored null), `1111`, `***1111`, 15 `*` + `1111`.
- CardNumber rejected: 16 `*` + `1111`, `111`, `11111`, `**11*1`, `abcd`, a 16-digit PAN, trailing newline, leading space, non-ASCII digits.
- Card values never appear in rejection reasons.
- Amount accepted: `-12.34`, `0`, `10.0`, `±9999999999999999.99`.
- Amount rejected: `12.345`, `1e3`, `12,34`, `abc`, empty, missing, leading space, `10000000000000000.00`.
- Time: `Z` accepted; `+02:00` converted to UTC; no offset rejected; unparseable rejected.
- `Now + 5min` accepted; `Now + 5min + 1 tick` rejected.
- Mismatched SourceSystem → rejected with key; matching or missing → valid.
- TransactionId `abc`, `""`, missing, int overflow, leading space, `1.0` → unparseable key.

### D3 — reconciliation (planner level; DB-level in step 2)
- New key → insert.
- New key already older than 24h → still inserted (finalized by the sweep in step 2).
- Changed fields on a mutable row → one field change per field, old/new formatted invariant.
- Unchanged mutable row → no update.
- Finalized row changed → ignored (counted), no update; unchanged → nothing counted.
- Active row past the window, not yet finalized, changed → ignored.
- Active in-window row missing → revoked.
- Active row past the window missing → not revoked.
- Revoked row still missing → nothing.
- Revoked in-window row reappears → reinstated, with its field changes.
- Revoked row past the window reappears → stays Revoked; ignored only if changed.
- Revoked in-window row reappears with an invalid record → stays Revoked, not revoked again.
- TransactionTime correction on a mutable row is applied even if it moves the row out of the window.
- Correction that would move a locked row into the window → ignored (lock decided by the stored time).
- Future-dated row within tolerance is mutable.
- Clock read once per run (service level, step 2).
- Finalizer runs even when every source failed (service level, step 2).

### 24h boundary
- Row at exactly `Now - 24h`: locked — not revoked when missing, change ignored, not a revocation candidate (and finalized, step 2).
- Row at `Now - 24h + 1 tick`: mutable — revoked when missing, change applied.
- Revoked row at exactly `Now - 24h` reappears → not reinstated; at `+1 tick` → reinstated.
- Clock and row times use fractional seconds; SQL load and finalize queries checked at the same boundary (step 2).

### D4 — revocation safety
- Rejected record for an existing in-window key → row unchanged, not revoked.
- Unparseable key → valid updates applied, missing row not revoked, `RevocationSkipped`.
- Duplicate key → all its records rejected, existing row unchanged and not revoked, `RevocationSkipped`.
- Guard: 10 candidates / 6 missing trips; 10 / 5 passes (50% is not > 50%); 9 / 9 passes (below minimum); disabled passes at 100%.
- Empty snapshot with ≥ 10 candidates trips; with fewer, all are revoked (accepted tradeoff).
- Source throws → that source Failed, others applied, run PartiallyFailed (step 2).
- Exception during apply rolls back that source only (step 2).

### D5 — idempotency (step 2, against SQLite)
- Second run with identical snapshots: every `Transactions` column unchanged (incl. `UpdatedAt`), no new audits, all counts zero.
- Re-run after a revocation, and after finalization → no new audits.
- Formatting-only differences (`10.0` vs `10.00`, `""` vs missing card, `+00:00` vs `Z`) → no update (also covered at planner level in step 1).

### D8 — lock and 409 (step 3)
- Second `RunAsync` while the first is blocked in a fake source → AlreadyRunning, no `ImportRun` row.
- Two `ImportService` instances from different scopes share the lock.
- Lock released after success, source failure, and unexpected exception; an unexpected exception leaves the run Failed, not Running.
- `POST /api/import` → 409 while running, 200 + summary otherwise (WebApplicationFactory).

## Implementation steps

1. ✅ Pure parts: `RunClock`, `ImportOptions`, `TransactionValidator`, `SnapshotParser`, `ReconciliationPlanner`, `RevocationGuard`, with unit tests.
2. ✅ `PlanApplier`, `Finalizer`, `ImportService`, lock, with SQLite integration tests.
3. DI registration, config, `ImportController`, mock sources, API tests.
