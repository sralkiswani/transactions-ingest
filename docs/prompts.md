# Prompts

The prompts given to Claude Code, in order. Typos, grammar and capitalization were fixed, and `-` bullet points were added, so they read clearly. Each heading is the step's commit message, followed by a one-line summary. P3 and P7 were one-click replies. The full reasoning is in [decisions.md](decisions.md) and [import-plan.md](import-plan.md). The two template commits before P1 have no prompt.

## P1 — add CLAUDE.md with working rules

Set the rules before any code: challenge ideas, separate verified from assumed, and never commit.

```text
Create CLAUDE.md with working rules for you, fitted to this fresh template:
- Be a collaborator, not a yes-man: challenge me, give options and tradeoffs before big decisions, and ask only when the answer changes the design.
- Separate verified from assumed. Run the build and tests before claiming something works, and list only commands you've run. Report template breakage; don't fix it yet.
- Small, scoped steps. Flag out-of-scope changes instead of making them.
- Record settled decisions in docs/decisions.md.
- Prefix replies with my prompt number (P1, P2, …).
- Stage with git add; never commit or rewrite history.
```

## P2 — add ticket summary to docs

Named the risks before designing, starting with a failing source that must never look like an empty snapshot and get all its transactions revoked.

```text
Save the ticket as docs/ticket.md. Already decided:
- A thin API, a class library for domain, import and EF, and an xUnit project. One ITransactionSource per source, TimeProvider injected.
- Statuses Active, Revoked and Finalized. A revoked transaction that reappears within 24h is reinstated; changes to finalized rows are ignored and logged.
- A concurrent import gets 409.

Design these risks out:
- A source that throws must never look like an empty snapshot and revoke everything.
- Mock timestamps computed from "now" on every call would break idempotency.
- A rejected record's key is still present, so it must not be revoked.
- EF Core on SQLite stores decimal as TEXT and drops DateTime's UTC kind.

Propose the schema (with the audit row), the transaction scope (I lean per source), and migrations vs EnsureCreated. Options and tradeoffs, then your recommendation. No code.
```

## P3 — record design decisions

Accepted the design (D1–D11): revocation and finalization share one 24h rule with `now` read once per run, and each source imports in its own database transaction.

```text
Accept all
```

## P4 — refine card, amount, guard and lock decisions

Corrected the design: accept the ticket's masked `***1111` card numbers, apply the mass-revocation guard only from 10 rows, and move the lock into the service so a scheduler respects it.

```text
Corrections to the design:
- D2: the ticket's sample sends CardNumber "***1111", so accept empty, or optional asterisks plus 4 digits. Allow negative amounts (refunds), and reject times more than 5 minutes in the future.
- D4: the revocation guard needs a minimum candidate count and an off switch, or removing 1 of 3 demo rows trips it.
- D8: take the lock inside the import service, not the controller, so a scheduled job respects it too. The controller maps "already running" to 409.

Keep requirements in ticket.md and decisions in decisions.md. Docs only.
```

## P5 — clean up template and fix client test setup

Fixed the template's broken client tests and removed its sample code in a separate step, before building on it.

```text
Keep empty CardNumber as null. Guard defaults: 10 candidates and >50%.

Now fix the template before building on it: client tests on Vitest with the broken spec fixed, the WeatherForecast sample and unused packages removed, the dev proxy on /api, and SQLite files gitignored. Done when dotnet build, npm run build and ng test all pass.
```

## P6 — add core library, test project and persistence model

Stored amounts as cents and enforced UTC, because EF Core on SQLite can't compare decimals and drops the UTC kind.

```text
Add the Core class library and an xUnit test project. In Core: the entities and DbContext per D6, amounts as long cents and UTC enforced on write and set on read (D7), and an initial migration applied on startup (D9). No FluentAssertions, since v8 changed its license.

Test on in-memory SQLite with the real migrations: no pending model changes, amounts round-trip and sort exactly (negatives included), non-UTC writes throw, and the unique key holds.
```

## P7 — record test framework decision

Chose xunit.v3 on Microsoft.Testing.Platform (D12).

```text
Record it
```

## P8 — add reply rules, trim test packages, quiet EF logging

Asked for the import plan before any code, with the 24h cutoff computed in C# and never in SQL, and dropped the unused VSTest packages that broke coverage.

```text
CLAUDE.md: no suggested-reply buttons, and end every reply with Verified, Assumed, Staged and Open questions.

Drop the VSTest packages Microsoft.Testing.Platform doesn't need, keeping coverage working, and stop logging SQL on every request.

Then plan the import, no code: the classes and where each D-rule lives, and a one-line test list covering D2–D5, D8 and the exact 24h boundary. Compute the cutoff in C#; no date arithmetic on columns in SQL.
```

## P9 — add import plan and pure import components

Built the pure import logic and planted three bugs, one at exactly 24h, to prove the tests catch them.

```text
Good plan. A revoked row whose reappearing record is invalid stays Revoked; 24h is a constant, and the future tolerance and guard settings go in config. Save the plan as docs/import-plan.md with three additions:
- "Ignored" counts only locked rows whose data changed, so a re-run reports all zeros.
- The service's finally marks a run still Running as Failed.
- Mock sources read JSON numbers from the raw text, never through double.

Build the pure parts first (clock, validator, parser, planner, guard, options) with their unit tests. Prove the tests catch bugs: temporarily change the 24h comparison from > to >=, count rejected keys as absent, and trip the guard at exactly 50%. Show the failures, then revert.
```

## P10 — add import service, applier, finalizer and lock

Gave each source one database transaction, opened only after its full snapshot is fetched, so a failing source changes nothing.

```text
SourceSystem must match exactly, case included. A record without one is accepted, since the source assigns it.

Build the database side: the applier, finalizer, lock and import service, with the plan's integration tests on SQLite using fake sources and a fake clock. Prove the tests catch bugs: write UpdatedAt on every row, treat a throwing source as an empty snapshot, and skip releasing the lock on an exception.
```

## P11 — wire import into API with mock sources and endpoints

Wired up the API, made config errors fail at startup, and gave mock files fixed timestamps so repeated imports see identical data.

```text
Wire it up:
- Register the import with its options validated on start, and fail on start if two sources share a name.
- Mock sources (D10): one JSON file per source in the ticket's shape, generated once if missing, including a card-less payment, a row older than 24h and the same TransactionId in two sources.
- POST /api/import (200 with the summary, or 409), GET /api/transactions (paged, filtered by source and status, newest first), GET /api/transactions/{id}/audit, and ProblemDetails for errors.
- API tests with WebApplicationFactory: import then list, a second import changes nothing, 409 while running, paging validation, and 404 for an unknown audit id.

Prove the 409 test by returning 200, then revert. Run the server and show me a real import and the first page.
```

## P12 — add transactions UI and align mock data with ticket sample

Built the Angular UI over server-side paging and made amounts keep two decimals.

```text
Make the mock files match the ticket's sample (sourceSystem on every record; UPTOWN, DOWNTOWN and ONLINE), and fix the converter so amounts always have 2 decimals (18.50, not 18.5).

Then the UI: a table with the ticket's columns and times shown in UTC, server-side paging with source and status filters, an Import button that shows the summary or the 409, a row click for audit history, loading, empty and error states, and plain CSS that works on a narrow screen. Test the HTTP calls with HttpTestingController, and confirm the table loads through the dev proxy.
```

## P13 — add README, prompt log and AI usage notes

Wrote the README, listed the corrections to the AI's output, and verified it from a fresh clone.

```text
Keep amounts as JSON numbers; the ~90 trillion limit is a known issue.

Write the README: what it does, prerequisites, build, test and run from Visual Studio and the CLI, a demo by editing the source files, the API, the layout, a summary of the decisions linking decisions.md and import-plan.md, and known limitations. Add an AI usage section listing my corrections to your output: the card rule, the guard on small data, the lock's placement, the test packages, a run left Running, JSON numbers through double, 2-decimal amounts and duplicate source names. Collect every prompt in docs/prompts.md.

Delete the template CHANGELOGs, check the https profile starts both apps, and verify the README from a fresh clone.
```

## P14 — raise SPA proxy timeout and add build-process summary

Raised the SPA proxy wait to 300 seconds after a 160-second cold start.

```text
Raise the SPA proxy timeout to 300 seconds. Find the real property name in the package, don't guess, and verify it in the generated spa.proxy.json. Add a short "How this was built" section after AI usage, with facts from the history only.
```

## P15 — fix Docker profile ports, Dockerfile and container data

Pinned the container's host ports so the Angular dev proxy can reach the API, and moved the database to a writable `/data` folder.

```text
In the Visual Studio container profile, the host's Angular proxy gets ECONNREFUSED because the container publishes the API on random host ports. Make Docker work properly:
- Pin the host ports to 7103 and 5291, using the real launchSettings property names.
- The Dockerfile restores before copying Core, and its Node may be too old for Angular 22. Fix both.
- A Release container never generates mock files, and /app may not be writable. I lean towards a config flag for generating them, on in Development and set by the container, with the data paths pointed at a writable folder. Challenge it if there's a cleaner way.

Verify with a real docker build and run, and add D14.
```
