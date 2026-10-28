# CLAUDE.md

Working rules for Claude in this repo. Keep this file short; update it when the stack or commands change.

## Stack

- `transactions-ingest.Server/` — ASP.NET Core Web API, .NET 10 (`net10.0`), controllers + OpenAPI, SPA proxy to the client in Development.
- `transactions-ingest.client/` — Angular 22, NgModule-based (`standalone: false` in schematics), `@angular/build` builders, npm.
- `transactions-ingest.slnx` — solution containing both projects (`.esproj` for the client).
- Still the WeatherForecast template sample. No .NET test project yet.

## Commands (verified 2026-10-28, Windows / PowerShell)

Run from the repo root unless noted.

| What | Command | Status |
|---|---|---|
| Build solution | `dotnet build transactions-ingest.slnx` | ✅ passes |
| Build client | `cd transactions-ingest.client; npm run build` | ✅ passes |
| Client tests | `cd transactions-ingest.client; npx ng test --watch=false` | ❌ broken in template (see below) |
| .NET tests | — | none exist yet |

Not yet run: `dotnet run` / `npm start` (dev server with SPA proxy). Add here once verified.

Dev ports (from config, not verified): API `https://localhost:7103` / `http://localhost:5291`, Angular dev server `https://localhost:54423`.

## Known template issues (flagged, not fixed)

- `angular.json` test target uses `@angular/build:unit-test` (Vitest) but passes `karmaConfig` → schema validation fails, tests don't run.
- `karma.conf.js` references Karma/Jasmine packages that aren't installed; `package.json` has Vitest + jsdom instead. `.esproj` says `JavaScriptTestFramework=Jasmine`.
- `src/app/app.spec.ts` ends with `};` instead of `});` — syntax error.
- `package.json` lists `jest-editor-support` and `run-script-os` as runtime deps with `*` versions.
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
