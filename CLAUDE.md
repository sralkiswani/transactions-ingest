# CLAUDE.md

Working rules for Claude in this repo. Keep this file short; update it when the stack or commands change.

## Stack

- `transactions-ingest.Server/` — ASP.NET Core Web API, .NET 10 (`net10.0`), controllers + OpenAPI, SPA proxy to the client in Development.
- `transactions-ingest.client/` — Angular 22, NgModule-based (`standalone: false` in schematics), `@angular/build` builders, npm, no zone.js (signals). One `App` component (transactions table, filters, paging, import, audit) over `TransactionsApi` (D13).
- `transactions-ingest.Core/` — class library: domain entities (`Domain/`), EF Core on SQLite (`Persistence/`: DbContext, D7 converters, migrations), import logic (`Import/`, see `docs/import-plan.md`; registered with `AddTransactionsImport`), JSON-file mock sources (`Import/Mock/`).
- `transactions-ingest.Tests/` — xUnit v3 on Microsoft.Testing.Platform (opted in via `global.json`; no VSTest packages, coverage via `Microsoft.Testing.Extensions.CodeCoverage`). Persistence and import tests use in-memory SQLite + real migrations; API tests use `WebApplicationFactory` with a temp SQLite file and source folder per test.
- `transactions-ingest.slnx` — solution with all four projects (`.esproj` for the client).
- Server reads `ConnectionStrings:Transactions` (relative paths resolve against the content root), runs `Migrate()` on startup, and generates missing mock source files under `transactions-ingest.Server/data/sources/` when `Import:MockSources:GenerateMissing` is on (Development and the container image; gitignored; delete to reset). API: `POST /api/import`, `GET /api/transactions`, `GET /api/transactions/{id}/audit`. HTTPS redirection runs only when the server is listening on an `https://` address (`HttpsEndpoints`, decided on the first request), so HTTP-only runs (container, API-only) don't log the "https port" warning. The dev proxy forwards `/api`.
- `dotnet-ef` is a local tool (`dotnet-tools.json`, pinned to the EF Core version).

## Commands (verified 2026-10-28, Windows / PowerShell)

Run from the repo root unless noted.

| What | Command | Status |
|---|---|---|
| Build solution | `dotnet build transactions-ingest.slnx` | ✅ passes |
| Build client | `cd transactions-ingest.client; npm run build` | ✅ passes |
| Client tests (Vitest + jsdom) | `cd transactions-ingest.client; npx ng test --watch=false` | ✅ passes (~1 min, mostly jsdom startup) |
| .NET tests | `dotnet test --solution transactions-ingest.slnx` | ✅ passes (MTP syntax; the positional `dotnet test <sln>` form is rejected) |
| .NET coverage | `dotnet test --solution transactions-ingest.slnx --coverage --coverage-output-format cobertura --results-directory <dir>` | ✅ writes a Cobertura XML for `transactions-ingest.Core` |
| Add a migration | `dotnet tool restore; dotnet ef migrations add <Name> --project transactions-ingest.Core --output-dir Persistence/Migrations` | ✅ used for `InitialCreate` |
| Run API only | `$env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5291'; dotnet run --project transactions-ingest.Server --no-launch-profile` | ✅ applies migrations, creates `transactions-ingest.Server/transactions.db`, generates mock source files, serves `/openapi/v1.json` |
| Docker Compose (quick start) | `docker compose up --build`, then `docker compose exec app sed -i 's/24.99/29.99/' /data/sources/store-uptown.json`; `docker compose down -v` | ✅ page loads on `http://localhost:8080`; Import works; the edit shows as Updated 1 with an audit entry; `down -v` removes container, network and volume (image stays) |
| Docker image (Release) | `docker build -f transactions-ingest.Server/Dockerfile -t transactions-ingest .` then `docker run --rm -p 8080:8080 transactions-ingest` | ✅ Angular + API on `http://localhost:8080`; import and list work; data in `/data` (D14) |
| Import + list (API running) | `curl -X POST http://localhost:5291/api/import` then `curl "http://localhost:5291/api/transactions?pageSize=10"` | ✅ 200 with run summary; first page newest first |

| Angular dev server against that API | `cd transactions-ingest.client; $env:ASPNETCORE_URLS='http://localhost:5291'; npm start` | ✅ serves `https://localhost:54423`; `/api` proxied to the API; table loads |

`--no-launch-profile` skips the SPA proxy, so no Angular dev server starts with the API; start it separately as above. `ASPNETCORE_URLS` tells `proxy.conf.js` where the API is. The `https` launch profile (`dotnet run --project transactions-ingest.Server --launch-profile https`) starts the API on `https://localhost:7103` and `npm start` via the SPA proxy; `/` redirects to `https://localhost:54423` (verified; a cold first start took ~160s, a warm start 14s). The proxy's wait is `SpaProxyTimeoutInSeconds` = 300 in the Server csproj (package default 120); it ends up as `MaxTimeoutInSeconds` in `bin/.../spa.proxy.json`.

Dev ports (verified: `http://localhost:5291` and `https://localhost:54423`): API `https://localhost:7103` / `http://localhost:5291`, Angular dev server `https://localhost:54423`.

## Known issues (flagged, not fixed)

- Client tests don't appear in VS Test Explorer. The `.esproj` has no `JavaScriptTestFramework`: VS's Angular adapter is Karma-only, and its Vitest adapter runs plain Vitest without the Angular compiler. Run them with `ng test`.
- `npm install` warns that install scripts were blocked for `@parcel/watcher`, `esbuild`, `lmdb`, `msgpackr-extract` (npm `allowScripts`). Builds and tests pass regardless.
- EF Core wraps exceptions from value converters in `DbUpdateException`; the D7 guards surface as its `InnerException`.
- Amounts reach the browser as JSON numbers, so values above ~90 trillion would lose precision in JavaScript (decimal(18,2) allows more).
- `git` is not on PATH in this shell; use VS's bundled git: `C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe`.

## How we work

- **Collaborator, not yes-man.** Challenge ideas, say when something is wrong, give options + tradeoffs before big decisions. Ask only when the answer changes the design.
- **Verified vs assumed.** Say which is which. Run build and tests before claiming something works. Only list commands here that have actually been run.
- **Template breakage:** report it, don't fix it unless asked.
- **Small, scoped steps.** Out-of-scope things worth changing get flagged, not changed.
- **Decisions:** when a design decision is settled, add a brief entry to `docs/decisions.md` (date, decision, why, alternatives considered).
- **Prompt numbering:** the user numbers prompts (P1, P2, …). Prefix each reply with the same number.
- **No suggested-reply buttons.** Put open questions in a numbered list; the user answers in a numbered prompt.
- **Reply footer.** End every reply with these sections: **Verified** (what was run and the result), **Assumed**, **Staged** (files), **Open questions**.

## Git

- Stage changes with `git add` (including new files).
- Never commit, amend, rebase, force-push, or otherwise rewrite history. The user reviews and commits.
