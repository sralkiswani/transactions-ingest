# Ticket: Transactions Ingest

- Import card transaction snapshots from 3 sources (Store Uptown, Store Downtown, Online) into SQLite. Key is (SourceSystem, TransactionId); IDs are only unique per source.
- Sources publish corrections, late transactions and removals. The import inserts new records, updates changed ones with field-level audit history, and marks a transaction Revoked (with an audit entry) if it's missing from its source's snapshot within 24h of TransactionTime. Optional: finalize transactions older than 24h so they become immutable.
- The import must be idempotent. An external scheduler will run it hourly; for now it's triggered manually via POST /api/import, and a scheduled job must be able to reuse the logic without changes.
- Fields: SourceSystem, CardNumber (last 4 digits, may be empty), LocationCode, ProductName are all strings with length < 20. TransactionId int, Amount decimal(18,2), TransactionTime UTC. Extra entities and fields are fine.
- GET /api/transactions feeds an Angular table: Source System, Transaction ID, Location Code, Product Name, Amount, Transaction Time, Status. Optional: sorting, filtering, paging, an audit view, an import button.
- Mocked sources are fine. Stack: ASP.NET 10, EF Core code-first, appsettings.json config, logging, error handling, tests.
- README with build/run steps, design decisions and assumptions, and AI usage; prompts and design discussions kept as markdown in the repo.

## Decided

- Thin Web API project, a class library for domain, import and EF, and an xUnit test project. One ITransactionSource per source. TimeProvider injected.
- Statuses are Active, Revoked and Finalized. A revoked transaction that reappears inside 24h is reinstated and audited. Finalization is in; changes to finalized rows are ignored and logged.
- A concurrent import gets 409. Single instance for now.

## Risks to design out

- A source that throws must never look like an empty snapshot and revoke everything.
- Mock timestamps computed from "now" on every call would show up as changes on every run and break idempotency.
- A record we reject for validation still means its key is present in the snapshot, so it must not get revoked.
- EF Core on SQLite: decimal is stored as TEXT, and DateTime comes back without the UTC kind.
