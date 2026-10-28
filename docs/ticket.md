# Ticket: Transactions Ingest

- Import card transaction snapshots from 3 sources (Store Uptown, Store Downtown, Online) into SQLite. Key is (SourceSystem, TransactionId); IDs are only unique per source.
- Sources publish corrections, late transactions and removals. The import inserts new records, updates changed ones with field-level audit history, and marks a transaction Revoked (with an audit entry) if it's missing from its source's snapshot within 24h of TransactionTime. Optional: finalize transactions older than 24h so they become immutable.
- The import must be idempotent. An external scheduler will run it hourly; for now it's triggered manually via POST /api/import, and a scheduled job must be able to reuse the logic without changes.
- Fields: SourceSystem, CardNumber (last 4 digits, may be empty), LocationCode, ProductName are all strings with length < 20. TransactionId int, Amount decimal(18,2), TransactionTime UTC. Extra entities and fields are fine.
- GET /api/transactions feeds an Angular table: Source System, Transaction ID, Location Code, Product Name, Amount, Transaction Time, Status. Optional: sorting, filtering, paging, an audit view, an import button.
- Mocked sources are fine. Stack: ASP.NET 10, EF Core code-first, appsettings.json config, logging, error handling, tests.
- README with build/run steps, design decisions and assumptions, and AI usage; prompts and design discussions kept as markdown in the repo.

## Sample source data

Each source publishes a JSON array of objects like:

```json
{"sourceSystem": "Store Uptown", "transactionId": 100001, "cardNumber": "***1111", "locationCode": "UPTOWN", "productName": "Wireless Mouse", "amount": 24.99, "transactionTime": "2026-02-27T08:15:30Z"}
```

The other two sources are `Store Downtown` / `DOWNTOWN` and `Online` / `ONLINE`.

Design decisions live in [decisions.md](decisions.md).
