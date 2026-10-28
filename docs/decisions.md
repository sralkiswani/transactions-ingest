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
  - `SourceSystem` is set by the source, not taken from the record. A record with no SourceSystem is accepted; one whose SourceSystem differs from the source's name (exact, case-sensitive comparison, since names come from our own config) is rejected.
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
- **Tradeoff:** The guard deliberately departs from the ticket's revoke-if-missing rule: a genuine mass removal is held back and the source fails until someone checks it. Setting `Import:RevocationGuard:Enabled` to `false` restores the literal rule.

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

- **Decision:** `Amount` stored as `long` cents via a value converter, read back with scale 2 (`cents * 0.01m`), so the API always returns two decimals (`18.50`, not `18.5`). All `DateTime` values go through one convention: reject non-UTC on write, mark UTC on read; default TEXT storage.
- **Why:** EF Core cannot sort or compare `decimal` (and `DateTimeOffset`) on SQLite; default decimal-as-TEXT sorts wrongly; `double` loses precision.
- **Alternatives:** `double` (lossy); `DateTime` as ticks (sorts fine, unreadable in a DB browser).

## D8 — Transaction scope and concurrency (2026-10-28)

- **Decision:** One DB transaction per source, implemented as a single `SaveChanges` (rows, audits and the source's result row together); finalization in its own `SaveChanges` afterwards. The import service in the library acquires an in-memory lock (behind `IImportLock`) and reports "already running" if it is held; the controller only maps that result to 409. Assumption: every caller of the import (the endpoint or a scheduled job) runs in the same process as the API. The service's `finally` releases the lock and marks the `ImportRun` Failed if it is still Running, so an unexpected exception can't leave a run Running forever.
- **Why:** A failing source is isolated, and its revocations only compare against its own complete snapshot. Source keys never overlap, so cross-source atomicity buys nothing. Locking in the service means an in-process scheduled job is serialized with manual imports without any extra code. An in-memory lock can't go stale after a crash.
- **Alternatives:** One transaction per run (one bad source blocks all); per record (partial snapshot + revocation is unsafe). DB-based lease (needed if the scheduler runs in another process; brings stale-lock handling).

## D9 — Migrations (2026-10-28)

- **Decision:** EF Core migrations in the class library, applied with `Database.Migrate()` at startup. Tests use in-memory SQLite and also run `Migrate()`. EF InMemory provider is not used.
- **Why:** `EnsureCreated` can't evolve a schema without dropping the audit history. Running migrations in tests verifies them. The InMemory provider has no transactions and none of the SQLite type behavior.
- **Alternatives:** Migration bundle as a deploy step (needed once there are multiple instances).

## D10 — Mock sources (2026-10-28)

- **Decision:** Each mock source reads a JSON file with absolute UTC timestamps (`data/sources/<source>.json`). In Development, missing files are generated once relative to now and written to disk. Unit tests use in-code fake sources with a fake clock. JSON numbers are mapped to `RawTransaction` strings from the raw JSON text, never through `double`. File shape: the ticket's sample (docs/ticket.md), a JSON array of objects with `sourceSystem` on every record and the ticket's field names (property names matched case-insensitively; unknown properties ignored). Generated files use the ticket's location codes (`UPTOWN`, `DOWNTOWN`, `ONLINE`: the source name's last word in capitals). Sources and the folder come from `Import:MockSources` (`Names`, `Directory`); file name is a slug of the source name (`Store Uptown` → `store-uptown.json`). Generated files live under `transactions-ingest.Server/data/` and are gitignored. A missing file outside Development makes that source fail.
- **Why:** Timestamps never shift between runs, so imports stay idempotent. Editing the files demos revocation and corrections; deleting them resets.

## D11 — API shape (2026-10-28)

- **Decision:** `POST /api/import` runs synchronously and returns the run summary (409 ProblemDetails if one is running); it is cancelled by app shutdown, not by the client disconnecting. `GET /api/transactions` is paged server-side: `page` (default 1), `pageSize` (default 25, max 100), optional `source` and `status`, sorted by `TransactionTime` descending with `SourceSystem`, `TransactionId` as tie-breakers. Out-of-range or unknown values return 400 ValidationProblemDetails; an empty value means no filter. `GET /api/transactions/{id}/audit` returns the audit trail oldest first, 404 for an unknown id. All errors are ProblemDetails; outside Development the exception handler includes no exception details. Enums are serialized as strings.
- **Why:** Mock data is small; a 202-and-poll flow is unnecessary now.

## D12 — .NET test framework (2026-10-28)

- **Decision:** xunit.v3 on Microsoft.Testing.Platform (MTP), opted in via `test.runner` in `global.json`. Run with `dotnet test --solution transactions-ingest.slnx`. Plain xUnit asserts; no FluentAssertions. The test project references only `xunit.v3` and `Microsoft.Testing.Extensions.CodeCoverage`, sets `OutputType=Exe`, and has no VSTest packages (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `coverlet.collector`). Coverage: `--coverage`.
- **Why:** New project; xUnit v2 is maintenance-only. On the .NET 10 SDK, xunit.v3 4.x requires the MTP opt-in. xunit.v3 brings MTP itself; the VSTest packages were unused, and `coverlet.collector` is a VSTest collector that does not run under MTP (`--coverage` failed with it). FluentAssertions v8 changed its license.
- **Alternatives:** xUnit v2 on VSTest (template default, no `global.json`, but a dead-end version). `coverlet.MTP` for coverage (also works; Microsoft's extension chosen as first-party and VS-integrated).

## D13 — Frontend (2026-10-28)

- **Decision:** One NgModule-declared `App` component (template style kept), `provideHttpClient()` instead of the deprecated `HttpClientModule`, signals for all state (the app runs without zone.js), a thin `TransactionsApi` service. No UI library; plain CSS with a card layout under 640px. Paging and filters go to the API; a filter change returns to page 1, and a newer list request cancels an older one. Times shown with `DatePipe` in UTC and labelled "(UTC)"; amounts with `DecimalPipe` `1.2-2`. The source filter options are the ticket's three names, hard-coded in the client (the API has no sources endpoint). Import shows the run summary, or the ProblemDetails title/detail on 409 and other errors, then reloads the table. Clicking (or Enter on) a row loads its audit history below the table.
- **Why:** Small app; one component keeps it readable. Signals make change detection explicit without zone.js.
- **Alternatives:** Separate table/audit components (more files for little gain now); a sources endpoint (worth adding if sources become dynamic).

## D14 — Containers (2026-10-28)

- **Decision:**
  - Generating missing mock files is a config flag, `Import:MockSources:GenerateMissing`, not an environment check. It is off in `appsettings.json`, on in `appsettings.Development.json`, and on in the image through an env var.
  - The Release image's final stage sets `ConnectionStrings__Transactions=Data Source=/data/transactions.db` and `Import__MockSources__Directory=/data/sources`. `/data` is created and owned by the non-root app user. It keeps data apart from the published app and can be mounted as a volume. (`/app` happens to be owned by that user too, because the template's `WORKDIR /app` runs after `USER $APP_UID`, so this does not depend on `/app` being read-only.) A plain `docker run` works with no flags.
  - The image builds Angular with Node 24 LTS (Angular 22 requires `^22.22.3 || ^24.15.0 || >=26`; the template installed Node 20). The restore layer copies every referenced project file, including Core.
  - The Visual Studio "Container (Dockerfile)" profile pins host ports with `httpPort: 5291` and `sslPort: 7103` and sets `publishAllPorts: false`, so the Angular dev server's `/api` proxy and the URLs match the other profiles.
- **Why:** Production behaviour (developer exception page off, no OpenAPI) stays intact in the container while the demo still works out of the box. A missing file outside the container and outside Development still fails its source, as D4 intends. The env vars live in the final stage only, so Visual Studio's fast mode (base stage, project folder mounted at `/app`) keeps using the project folder like `dotnet run`.
- **Alternatives:** Running the container in Development (exposes developer error pages and OpenAPI). Baking sample files into the image at build time (they would age past the 24h window and be read-only). Putting the paths in `appsettings.Production.json` (would also apply to a non-container Production run, where `/data` may not exist).
