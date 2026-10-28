# Design decisions

Short records of settled decisions. Newest at the bottom. See [ticket.md](ticket.md) for requirements.

## D1 — Solution structure (2026-10-28)

- **Decision:** Thin Web API project; one class library for domain, import and EF Core; one xUnit test project. One `ITransactionSource` per source. `TimeProvider` injected everywhere time is read. The test project references Core, and Server for API tests through `WebApplicationFactory` (`Microsoft.AspNetCore.Mvc.Testing`).
- **Why:** Import logic must be reusable by a scheduled job without changes; the API only hosts it. API tests exercise the real pipeline (routing, DI, 409 mapping) rather than a controller in isolation.

## D2 — Field rules and validation (2026-10-28)

- **Decision:**
  - "Length < 20" means max 19 characters, enforced in validation (SQLite does not enforce lengths).
  - `CardNumber` is empty, or optional leading asterisks followed by exactly 4 digits (e.g. `1111`, `***1111`); anything else rejects the record. Non-empty values are stored as received; empty is stored as `null`. Card values are never logged.
  - `Amount` may be negative (refunds). More than 2 decimal places is rejected, not rounded.
  - `TransactionTime` more than 5 minutes after the run's `now` is rejected. The tolerance is configurable (`Import:FutureTolerance`, default 5 minutes).
  - `SourceSystem` is set by the source, not taken from the record; a mismatching record is rejected.
- **Why:** Accept the masked format sources actually send while never storing more than the last 4 digits; avoid hiding source errors; avoid `""`/`null` showing up as changes. A small future tolerance absorbs clock skew without letting far-future rows sit in the mutable window indefinitely.

## D3 — Statuses and the 24h window (2026-10-28)

- **Decision:**
  - `now` is read once per run and used for every source and for finalization.
  - A row is mutable while `now < TransactionTime + 24h`; revocation, reinstatement and finalization all use this one comparison. The 24h window is a constant (fixed by the ticket), not config.
  - Apply per source: insert new keys (including late ones); update changed mutable rows with field-level audit; ignore changes to locked rows (counted, not audited); revoke Active in-window keys missing from the snapshot; reinstate Revoked in-window keys that reappear with a valid record (applying any field changes). A Revoked row whose reappearing record fails validation stays Revoked (its key is present, so it is not revoked again either).
  - Finalization is a separate, time-based step after all sources: Active rows past the window become Finalized. Revoked rows stay Revoked and become locked after the window.
  - Ignored changes to finalized rows: one warning per source per run with a count; details at Debug.
- **Why:** One boundary rule means no gap or overlap. Finalizing regardless of source health loses nothing, since revocation and corrections stop at 24h anyway. Per-row warnings would repeat every hour forever.
- **Alternatives:** Finalize only after a successful source run (no benefit, rows could stay Active forever if a source is down).

## D4 — Revocation safety (2026-10-28)

- **Decision:**
  - The full snapshot is fetched before the source's DB transaction opens. A source that throws fails that source only; nothing is written or revoked.
  - Any record with a parseable key counts as present, even if its other fields fail validation.
  - If any key is unparseable or duplicated in a snapshot, valid upserts are applied but revocation is skipped for that source this run, and an error is logged.
  - Configurable guard: if a run would revoke more than a set percentage of a source's Active in-window rows, that source fails with no changes. The percentage only applies once there are at least a minimum number of Active in-window rows (candidates); below that the guard does not trip. The guard can be switched off in config. Enabled flag, minimum candidates and percentage all live in `appsettings.json`.
- **Why:** A failed, truncated or ambiguous snapshot must never look like mass removals.

## D5 — Idempotency (2026-10-28)

- **Decision:** A second run with unchanged snapshots writes nothing to `Transactions` or `TransactionAudits`; only import-run rows are added. No `LastSeenAt` column. Values are compared after normalization. "Ignored" counts only locked rows whose incoming data differs; unchanged rows count nothing, so a re-run reports all zeros.
- **Why:** Makes idempotency a precise, testable property.

## D6 — Schema (2026-10-28)

- **Decision:**
  - `Transactions`: surrogate `Id` PK, unique index on `(SourceSystem, TransactionId)`, ticket fields, `Status` stored as text, `CreatedAt`, `UpdatedAt`, `FinalizedAt?`, `RevokedAt?`.
  - `TransactionAudits`: one row per change — `TransactionPk`, `ImportRunId`, `ChangedAt`, `ChangeType` (Created/Updated/Revoked/Reinstated/Finalized), `FieldName?`, `OldValue?`, `NewValue?` (invariant-culture text).
  - `ImportRuns` (`StartedAt`, `CompletedAt?`, `Status`: Running/Succeeded/PartiallyFailed/Failed) and `ImportRunSources` (per source: status, error, counts for fetched/inserted/updated/revoked/reinstated/rejected/ignored, revocation-skipped flag).
- **Why:** Surrogate key keeps audit FKs and routes single-column. One audit row per field answers "history of field X" directly.
- **Alternatives:** Natural composite PK (two-column FKs); one audit row per event with a JSON diff (compact, harder to query per field).

## D7 — SQLite type mapping (2026-10-28)

- **Decision:** `Amount` stored as `long` cents via a value converter. All `DateTime` values go through one convention: reject non-UTC on write, mark UTC on read; default TEXT storage.
- **Why:** EF Core cannot sort or compare `decimal` (and `DateTimeOffset`) on SQLite; default decimal-as-TEXT sorts wrongly; `double` loses precision.
- **Alternatives:** `double` (lossy); `DateTime` as ticks (sorts fine, unreadable in a DB browser).

## D8 — Transaction scope and concurrency (2026-10-28)

- **Decision:** One DB transaction per source; finalization in its own transaction afterwards. The import service in the library acquires an in-memory lock (behind `IImportLock`) and reports "already running" if it is held; the controller only maps that result to 409. Assumption: every caller of the import (the endpoint or a scheduled job) runs in the same process as the API. The service's `finally` releases the lock and marks the `ImportRun` Failed if it is still Running, so an unexpected exception can't leave a run Running forever.
- **Why:** A failing source is isolated, and its revocations only compare against its own complete snapshot. Source keys never overlap, so cross-source atomicity buys nothing. Locking in the service means an in-process scheduled job is serialized with manual imports without any extra code. An in-memory lock can't go stale after a crash.
- **Alternatives:** One transaction per run (one bad source blocks all); per record (partial snapshot + revocation is unsafe). DB-based lease (needed if the scheduler runs in another process; brings stale-lock handling).

## D9 — Migrations (2026-10-28)

- **Decision:** EF Core migrations in the class library, applied with `Database.Migrate()` at startup. Tests use in-memory SQLite and also run `Migrate()`. EF InMemory provider is not used.
- **Why:** `EnsureCreated` can't evolve a schema without dropping the audit history. Running migrations in tests verifies them. The InMemory provider has no transactions and none of the SQLite type behavior.
- **Alternatives:** Migration bundle as a deploy step (needed once there are multiple instances).

## D10 — Mock sources (2026-10-28)

- **Decision:** Each mock source reads a JSON file with absolute UTC timestamps (`data/sources/<source>.json`). In Development, missing files are generated once relative to now and written to disk. Unit tests use in-code fake sources with a fake clock. JSON numbers are mapped to `RawTransaction` strings from the raw JSON text, never through `double`.
- **Why:** Timestamps never shift between runs, so imports stay idempotent. Editing the files demos revocation and corrections; deleting them resets.

## D11 — API shape (2026-10-28)

- **Decision:** `POST /api/import` runs synchronously and returns the run summary (409 if one is running). `GET /api/transactions` is paged server-side from the start.
- **Why:** Mock data is small; a 202-and-poll flow is unnecessary now.

## D12 — .NET test framework (2026-10-28)

- **Decision:** xunit.v3 on Microsoft.Testing.Platform (MTP), opted in via `test.runner` in `global.json`. Run with `dotnet test --solution transactions-ingest.slnx`. Plain xUnit asserts; no FluentAssertions. The test project references only `xunit.v3` and `Microsoft.Testing.Extensions.CodeCoverage`, sets `OutputType=Exe`, and has no VSTest packages (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `coverlet.collector`). Coverage: `--coverage`.
- **Why:** New project; xUnit v2 is maintenance-only. On the .NET 10 SDK, xunit.v3 4.x requires the MTP opt-in. xunit.v3 brings MTP itself; the VSTest packages were unused, and `coverlet.collector` is a VSTest collector that does not run under MTP (`--coverage` failed with it). FluentAssertions v8 changed its license.
- **Alternatives:** xUnit v2 on VSTest (template default, no `global.json`, but a dead-end version). `coverlet.MTP` for coverage (also works; Microsoft's extension chosen as first-party and VS-integrated).
