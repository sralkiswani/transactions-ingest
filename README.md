# Transactions Ingest

Imports card transaction snapshots from three sources (Store Uptown, Store Downtown, Online) into SQLite and shows them in an Angular table.

Each import compares every source's current snapshot with what is stored:

- new transactions are inserted;
- changed ones are updated, with one audit row per changed field;
- transactions missing from their source's snapshot within 24 hours of `TransactionTime` are marked **Revoked**;
- a revoked transaction that reappears within that window is **reinstated**;
- transactions older than 24 hours are **Finalized** and never change again.

Running the same import twice changes nothing. Imports run on demand through `POST /api/import`; the same `ImportService` can be called by a scheduled job.

The requirements are in [docs/ticket.md](docs/ticket.md).

## Prerequisites

- .NET SDK 10 (built with 10.0.402)
- Node.js with npm (built with Node 26.12.1, npm 12.2.1)
- An ASP.NET Core HTTPS development certificate. `npm start` exports it automatically for the Angular dev server. To make browsers trust it, run once:

  ```bash
  dotnet dev-certs https --trust
  ```

- For Visual Studio: Visual Studio 2026 with the *ASP.NET and web development* workload, which includes JavaScript/TypeScript project support.

The commands below are for PowerShell on Windows, run from the repository root.

## Build and test

### From the CLI

```bash
dotnet build transactions-ingest.slnx
```

```bash
dotnet test --solution transactions-ingest.slnx
```

```bash
cd transactions-ingest.client; npm ci; npm run build; npx ng test --watch=false
```

- **Build:** `dotnet build` builds the API, the library, the tests and the client project. The client project builds through Visual Studio's JavaScript SDK. On a fresh clone it also runs `npm install` for the client, so the first build takes about a minute longer. `npm ci` in the client command then gives a clean, lockfile-exact install.
- **.NET tests:** they run on Microsoft.Testing.Platform, switched on in `global.json`. Use the `--solution` form shown; `dotnet test <sln>` without it is rejected. Add `--coverage --coverage-output-format cobertura` for a coverage report.
- **Client tests:** they use Vitest with jsdom. They take about a minute, mostly starting jsdom.

### From Visual Studio

1. Open `transactions-ingest.slnx`.
2. Set `transactions-ingest.Server` as the startup project and pick the **https** launch profile.
3. Press F5. The API starts on `https://localhost:7103` and, through the SPA proxy, starts `npm start` for the Angular app on `https://localhost:54423`. The browser is redirected there once it is up.

   `npm start` runs in a separate console window. The SPA proxy waits up to 300 seconds for it, set by `SpaProxyTimeoutInSeconds` in the Server project. The package default of 120 seconds was shorter than one observed cold start of about 160 seconds.
4. Run the tests with the CLI commands above. Test Explorer has not been checked with the .NET tests, which run on Microsoft.Testing.Platform. The Angular tests don't appear there (see [Known limitations](#known-limitations)).

## Run

### One command (API and Angular together, via the SPA proxy)

```bash
dotnet run --project transactions-ingest.Server --launch-profile https
```

Open `https://localhost:7103`. It redirects to the Angular app on `https://localhost:54423`.

### Two terminals (API and Angular dev server separately)

API on `http://localhost:5291`:

```bash
$env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5291'; dotnet run --project transactions-ingest.Server --no-launch-profile
```

Angular dev server. `ASPNETCORE_URLS` tells its `/api` proxy where the API is:

```bash
cd transactions-ingest.client; $env:ASPNETCORE_URLS='http://localhost:5291'; npm start
```

Open `https://localhost:54423`.

### What happens on start

- The SQLite database `transactions-ingest.Server/transactions.db` is created and migrated.
- In Development, one JSON file per source is generated under `transactions-ingest.Server/data/sources/`, with timestamps relative to that moment. Files are only generated when missing.
- Both the database and the data files are gitignored.

Click **Import** to load the data. A second Import reports all zeros.

## Demo

The mock sources read `transactions-ingest.Server/data/sources/*.json` on every import:

- `store-uptown.json`
- `store-downtown.json`
- `online.json`

Each is an array of records in the ticket's shape, e.g. `{"sourceSystem": "Store Uptown", "transactionId": 100001, "cardNumber": "***1111", "locationCode": "UPTOWN", "productName": "Wireless Mouse", "amount": 24.99, "transactionTime": "2026-02-27T08:15:30Z"}`.

Edit a file, save it, then click **Import**:

| Try | Result |
|---|---|
| Change `amount` or `productName` of a record from the last 24 hours | **Updated 1**. Click the row: the audit shows the field with its old and new value. |
| Delete a record from the last 24 hours | **Revoked 1**. The row's status becomes Revoked. |
| Put the deleted record back | **Reinstated 1**. The row is Active again. |
| Change a record older than 24 hours (Store Uptown 100004 or Store Downtown 200003; both are Finalized on the first import) | **Ignored 1**, on every import until you undo the edit. Finalized rows never change. |
| Break a value, e.g. `"amount": 1.005` or `"cardNumber": "12345"` | **Rejected 1**. The stored row is unchanged and is *not* revoked. |
| Make a file invalid JSON | That source **Failed**. The other sources still import and nothing is revoked. |

The sample data also contains:
- a card-less payment (Store Downtown 200002);
- a refund (Online 300003);
- TransactionId 100001 in two sources.

**Reset:**
1. Stop the app.
2. Delete `transactions-ingest.Server/data/sources/*.json` and `transactions-ingest.Server/transactions.db*`.
3. Start again.

Delete both: new files on their own get new timestamps, so the old rows would look changed or removed. The generated rows age too. About a day after the files were generated, every row is past the 24-hour window and edits are ignored, so reset to get fresh data.

## API

| Method and path | Description |
|---|---|
| `POST /api/import` | Runs an import and returns the run summary: overall status, finalized count, and per source fetched, inserted, updated, revoked, reinstated, rejected and ignored counts plus any error. Returns **409** ProblemDetails if an import is already running. |
| `GET /api/transactions` | Paged list, newest `TransactionTime` first. Query: `page` (default 1), `pageSize` (default 25, max 100), `source`, `status` (`Active`, `Revoked`, `Finalized`). Invalid values return **400**. |
| `GET /api/transactions/{id}/audit` | Audit history of one transaction (by its `id` from the list), oldest first. Unknown id returns **404**. |

Errors are ProblemDetails; outside Development they carry no exception details. In Development, OpenAPI is served at `/openapi/v1.json`.

## Project layout

```
transactions-ingest.slnx
transactions-ingest.Server/     ASP.NET Core API: Program.cs, Controllers/, Contracts/ (DTOs), appsettings.json
transactions-ingest.Core/       Class library
  Domain/                       Entities and enums
  Persistence/                  DbContext, value converters (cents, UTC), migrations
  Import/                       RunClock, validator, snapshot parser, planner, guard, applier, finalizer,
                                lock, ImportService, options, DI registration (AddTransactionsImport)
  Import/Mock/                  JSON-file sources and the sample-file generator
transactions-ingest.Tests/      xUnit v3
  Persistence/                  SQLite mapping and migration tests
  Import/                       Unit tests (validation, planning, 24h boundary, guard, mock sources)
  Import/Service/               ImportService on in-memory SQLite with fake sources and a fake clock
  Api/                          WebApplicationFactory tests, each on its own SQLite file
transactions-ingest.client/     Angular 22 (NgModule): src/app/app.*, transactions-api.ts
docs/                           ticket.md, decisions.md, import-plan.md, prompts.md
CLAUDE.md                       Working rules for the coding agent
```

## Design decisions and assumptions

The full reasoning, with alternatives considered, is in [docs/decisions.md](docs/decisions.md) (D1 to D13). How the import is put together and tested is in [docs/import-plan.md](docs/import-plan.md). In short:

- **Structure (D1):** a thin API; the import, domain and EF Core live in a class library. The import can be reused by a scheduled job without changes. Time comes from an injected `TimeProvider`.
- **Validation (D2):** text fields are at most 19 characters ("< 20").
  - `CardNumber` must be empty, or up to 15 `*` followed by exactly 4 digits. Empty is stored as null.
  - Amounts may be negative but have at most 2 decimals; they are rejected, never rounded.
  - Times need an explicit zone and may be at most 5 minutes in the future.
  - A record's `sourceSystem` must match its source exactly, or be absent.
- **The 24h window (D3):** the time is read once per run. One rule, `TransactionTime > now − 24h`, decides what can still change, be revoked or be reinstated. Finalization runs after every import whatever happened to the sources.
- **Revocation safety (D4):**
  - A source that fails never looks like an empty snapshot.
  - A record that fails validation still counts as present.
  - Unparseable or duplicate keys skip revocation for that source.
  - A guard refuses to revoke more than 50% of a source's in-window rows once there are at least 10 of them. This deliberately departs from the ticket's revoke-if-missing rule, because a truncated snapshot looks the same as a mass removal; set `Import:RevocationGuard:Enabled` to `false` for the literal rule.
- **Idempotency (D5):** a re-run with unchanged data writes nothing to transactions or audits. "Ignored" counts only real changes to locked rows.
- **Storage (D6, D7, D9):**
  - a surrogate key plus a unique `(SourceSystem, TransactionId)` index;
  - one audit row per changed field;
  - amounts stored as integer cents and returned with 2 decimals;
  - all `DateTime` values enforced as UTC;
  - EF Core migrations applied on startup.
- **Transactions and concurrency (D8):** one DB transaction per source. An in-process lock inside `ImportService` makes a concurrent import return 409. A run that ends unexpectedly is marked Failed.
- **Mock sources (D10):** JSON files generated once, so timestamps don't drift between runs. JSON numbers are read from their raw text, never through `double`.
- **API and UI (D11, D13):** server-side paging and filters. The UI is one NgModule component using signals and plain CSS, with a card layout on narrow screens.

## Known limitations

- **Single instance.** The import lock is in memory, so a scheduler must call `POST /api/import` (or `ImportService` in the same process). Several instances or an out-of-process job would need a database lease (D8). Migrations also run at startup, which suits one instance only (D9).
- **Mock sources only.** There is no real source integration; a new source implements `ITransactionSource`.
- **Source filter list is hard-coded in the client.** The UI's source filter lists the three ticket sources; the API has no endpoint that lists sources.
- **Very large amounts.** Amounts are JSON numbers, so values above about 90 trillion would lose precision in the browser.
- **Angular tests aren't in VS Test Explorer.** Visual Studio's Angular test adapter only supports Karma, and its Vitest adapter can't compile Angular components. Run them with `npx ng test`.
- **SPA proxy cold start.** The first `npm start` can be slow; one cold start took about 160 seconds. The SPA proxy waits up to 300 seconds. If a start ever takes longer, the proxy logs "Unable to connect to the SPA development server", but the app still comes up; refresh the page.
- **No authentication or authorization.**

## AI usage

Claude Code (Anthropic) wrote the code and docs from my prompts.

- Each step was one prompt and one commit. [docs/prompts.md](docs/prompts.md) has every prompt, headed by its commit message and a one-line summary.
- [CLAUDE.md](CLAUDE.md) holds the working rules the agent followed:
  - verify before claiming;
  - small, scoped steps;
  - record decisions;
  - stage but never commit.
- I reviewed each step and committed it myself.
- The record of what I changed from the AI's output is in [docs/decisions.md](docs/decisions.md) and the commit history.

The main corrections:

| Correction | Where |
|---|---|
| The proposed `CardNumber` rule (exactly 4 digits) rejected the ticket's own sample `***1111`. | P4, D2 |
| The revocation guard would trip on small data (removing 1 of 3 rows). It now needs a minimum of 10 candidates and can be switched off. | P4, D4 |
| The concurrency lock was moved from the controller into `ImportService`, so a scheduled job respects it too. | P4, D8 |
| The test project kept VSTest packages it didn't use, and coverage didn't actually work under Microsoft.Testing.Platform. The packages were removed and the coverage tool replaced. | P8, D12 |
| An `ImportRun` could be left Running after a crash. The service now marks it Failed in `finally`. | P9, D8 |
| JSON numbers must not pass through `double`; they are read as raw text. | P9, D10 |
| Amounts lost their 2-decimal formatting (`18.5` instead of `18.50`). Fixed in the cents converter. | P12, D7 |
| Source names must be unique; startup now fails on a duplicate, instead of the import crashing mid-run. | P11 |

## How this was built

- **P1 – rules before code.** [CLAUDE.md](CLAUDE.md) set the working rules: separate what was verified from what was assumed, list only commands that were actually run, keep steps small, and log decisions. Template problems were reported rather than silently fixed; P5 fixed them.
- **P2 – design and risks before code.** The ticket's risks were written down first:
  - a failing source must not look like an empty snapshot;
  - mock timestamps must not drift between runs;
  - a rejected record must still count as present;
  - EF Core on SQLite stores decimals as text and loses the UTC kind on dates.

  The schema, the transaction scope and migrations were then proposed with options and tradeoffs, still without code. P3 accepted them as D1–D11.
- **P4 – corrections to the design.** The `CardNumber` rule was rejecting the ticket's own sample. The revocation guard got a minimum number of candidates and an off switch. The lock moved into the import service.
- **P8 – plan before implementation.** The import was planned first: its classes, where each D-rule lives, and a one-line test list covering D2–D5, D8 and the 24h boundary. The window cutoff is computed in C#, never in SQL. The same step removed unused test packages and fixed coverage.
- **P9 – corrections to the plan.** "Ignored" counts only real changes. A run left Running is marked Failed. JSON numbers are never read through `double`. The import was then built in three steps, P9–P11.
- **P9 – bug injection, pure logic.** Three bugs were planted on purpose: the 24h comparison changed from `>` to `>=`, rejected keys counted as absent, and the guard tripping at exactly 50%. Each had to make tests fail before being reverted.
- **P10 – bug injection, database and service.** Three more: `UpdatedAt` written on every row, a source that throws treated as an empty snapshot, and the lock not released after an exception.
- **P11 – API.** Startup fails on duplicate source names. The API tests run on their own databases. The 409 was swapped for a 200 to show a test catches it. The server was run for real and the import summary and first page were checked.
- **P12 – end-to-end UI check.** The API and the Angular dev server were run together, and the table was confirmed to load through the `/api` proxy. Amounts always show 2 decimals, and the mock data matches the ticket's sample.
- **P13 – clean-clone check.** The repository was cloned into a temp folder and the README followed exactly: build, all tests, and the app. The README was corrected where it was wrong.
- **P14 – the real setting, not a guess.** A cold first start took about 160 seconds, past the SPA proxy's 120-second default. The wait was raised to 300 seconds with the property name found in the package (`SpaProxyTimeoutInSeconds`), and checked in the generated `spa.proxy.json`.
