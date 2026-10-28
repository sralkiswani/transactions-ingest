# CLAUDE.md

Working rules for Claude in this repo. Keep this file short; update it when the stack or commands change.

## Stack

- `transactions-ingest.Server/` — ASP.NET Core Web API, .NET 10 (`net10.0`), controllers + OpenAPI, SPA proxy to the client in Development.
- `transactions-ingest.client/` — Angular 22, NgModule-based (`standalone: false` in schematics), `@angular/build` builders, npm.
- `transactions-ingest.Core/` — class library: domain entities (`Domain/`), EF Core on SQLite (`Persistence/`: DbContext, D7 converters, migrations).
- `transactions-ingest.Tests/` — xUnit v3 on Microsoft.Testing.Platform (opted in via `global.json`). Persistence tests use in-memory SQLite + real migrations.
- `transactions-ingest.slnx` — solution with all four projects (`.esproj` for the client).
- Server reads `ConnectionStrings:Transactions` (relative paths resolve against the content root) and runs `Migrate()` on startup. Client is a bare placeholder; the dev proxy forwards `/api`.
- `dotnet-ef` is a local tool (`dotnet-tools.json`, pinned to the EF Core version).

## Commands (verified 2026-10-28, Windows / PowerShell)

Run from the repo root unless noted.

| What | Command | Status |
|---|---|---|
| Build solution | `dotnet build transactions-ingest.slnx` | ✅ passes |
| Build client | `cd transactions-ingest.client; npm run build` | ✅ passes |
| Client tests (Vitest + jsdom) | `cd transactions-ingest.client; npx ng test --watch=false` | ✅ passes (~1 min, mostly jsdom startup) |
| .NET tests | `dotnet test --solution transactions-ingest.slnx` | ✅ passes (MTP syntax; the positional `dotnet test <sln>` form is rejected) |
| Add a migration | `dotnet tool restore; dotnet ef migrations add <Name> --project transactions-ingest.Core --output-dir Persistence/Migrations` | ✅ used for `InitialCreate` |
| Run API only | `$env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5291'; dotnet run --project transactions-ingest.Server --no-launch-profile` | ✅ applies migrations, creates `transactions-ingest.Server/transactions.db`, serves `/openapi/v1.json` |

`--no-launch-profile` skips the SPA proxy, so no Angular dev server starts. Not yet run: the launch profiles with the SPA proxy, and `npm start`.

Dev ports (from config; only `http://localhost:5291` verified): API `https://localhost:7103` / `http://localhost:5291`, Angular dev server `https://localhost:54423`.

## Known issues (flagged, not fixed)

- Client tests don't appear in VS Test Explorer. The `.esproj` has no `JavaScriptTestFramework`: VS's Angular adapter is Karma-only, and its Vitest adapter runs plain Vitest without the Angular compiler. Run them with `ng test`.
- `npm install` warns that install scripts were blocked for `@parcel/watcher`, `esbuild`, `lmdb`, `msgpackr-extract` (npm `allowScripts`). Builds and tests pass regardless.
- `HttpClientModule` in `app-module.ts` is deprecated in favor of `provideHttpClient()`.
- With `Logging:LogLevel:Default=Information`, EF Core logs every SQL command at startup and per request.
- EF Core wraps exceptions from value converters in `DbUpdateException`; the D7 guards surface as its `InnerException`.
- `CHANGELOG.md` files in both projects describe the original template setup (weather, Karma, jest-editor-support).
- `git` is not on PATH in this shell; use VS's bundled git: `C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe`.

## How we work

- **Collaborator, not yes-man.** Challenge ideas, say when something is wrong, give options + tradeoffs before big decisions. Ask only when the answer changes the design.
- **Verified vs assumed.** Say which is which. Run build and tests before claiming something works. Only list commands here that have actually been run.
- **Template breakage:** report it, don't fix it unless asked.
- **Small, scoped steps.** Out-of-scope things worth changing get flagged, not changed.
- **Decisions:** when a design decision is settled, add a brief entry to `docs/decisions.md` (date, decision, why, alternatives considered).
- **Prompt numbering:** the user numbers prompts (P1, P2, …). Prefix each reply with the same number.

## Git

- Stage changes with `git add` (including new files).
- Never commit, amend, rebase, force-push, or otherwise rewrite history. The user reviews and commits.
