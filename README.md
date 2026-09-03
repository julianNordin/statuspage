# statuspage

A status page for a handful of services: what is up, what is not, what happened when it was not,
and how much of the last ninety days each one has been available.

**Live:** [status page](https://white-ground-07fbe590f.5.azurestaticapps.net) · deployed to Azure
from this repository, and it stays up.

---

## Why this project

A status page is a small product with one property that makes it worth building: **the page that
reports on a system must not depend on that system.** A status page served by the API it monitors
tells you nothing at the only moment you need it.

So the public page reads a JSON snapshot from blob storage and touches neither the API nor the
database. That is the architecture, and it is also — not coincidentally — what makes the whole
thing run for nothing.

Three decisions carry the weight:

- **State is stored as intervals, not samples.** Ninety days of one-minute checks is roughly
  130,000 rows per component. Ninety days of *transitions*, for a service that stayed up, is
  five. Uptime is a sum over interval durations with maintenance subtracted, not a count over
  samples.
- **A component goes down after N consecutive failures, not one.** Hysteresis is what separates
  a status page from an alarm that cries wolf, and it is a state machine rather than an `if`.
- **Outages open themselves; only a person closes them.** A transition to Down opens an incident
  automatically. A return to Up posts an update and leaves the incident open, because whether
  something is over is a judgement and not a measurement.

## Architecture

```
Angular SPA  (Static Web Apps)
├── /          public status page ──reads──► status.json   (Blob, public + CORS)
└── /admin     operator console   ──calls──► API
                                              │
ASP.NET Core API  (Container Apps, minReplicas 0)
  ├── EF Core ──► Azure SQL (free offer, serverless, auto-pause)
  ├── writes config.json ──► Blob   (whenever an operator changes something)
  └── Key Vault via managed identity · Application Insights

Checker  (Container Apps Job, cron — separate image, same repo)
  ├── reads config.json from Blob            ← never reads SQL on a normal run
  ├── runs the checks, applies hysteresis
  ├── writes an interval to SQL ONLY on a state transition   ← rare, so SQL sleeps
  └── regenerates status.json ──► Blob
```

**SQL is the authoritative log; blob storage is a derived read model.** Rebuilding the snapshot
from the interval log is an admin operation, and it is tested.

The layering rule is enforced by project references, not by convention:

| Layer | May reference |
|---|---|
| `Controllers` | HTTP shapes only. No `DbContext`. |
| `Services` | orchestration, authorization, projection into blob |
| `Domain/` | **pure C#** — no EF, no HTTP, no `DateTime.Now`; `TimeProvider` is injected |
| `Infrastructure/` | the only place EF Core and the Blob SDK are named |

## The cost arithmetic

This is the part that decided the architecture, so it is written down with the numbers attached
rather than asserted.

Azure SQL's free offer allows **100,000 vCore-seconds per month**. At the 0.5-vCore serverless
floor that is about **55 hours of *awake* database**, and auto-pause needs 60 unbroken idle
minutes before it triggers.

A checker that read its configuration from SQL every ten minutes would never let it sleep:

```
730 hours  ×  0.5 vCore  ×  3600 seconds  =  1,314,000 vCore-seconds
                                             ~13× the monthly allowance
```

It would exhaust the grant in about four days, and then `AutoPause` stops the database for the
rest of the month. **That number is why the checker reads its configuration from blob storage.**
The database's only visitors are an operator making a change and a genuine state transition, and
both are rare enough that it sleeps.

Everything else fits inside a free grant with room to spare:

| Piece | Allowance | This project |
|---|---|---|
| Container Apps | 180,000 vCPU-s + 360,000 GiB-s + 2M requests | Checker ≈ 4,320 runs/mo at 0.25 vCPU; API scales to zero |
| Static Web Apps | 100 GB/mo, managed TLS | An Angular bundle |
| Azure SQL | 100,000 vCore-s, 32 GB | Asleep unless something changed |
| Blob Storage | — | Kilobytes, ~13,000 transactions/mo |
| App Insights | 5 GB/mo ingestion | Nowhere near |

A resource-group budget alerts on a forecast breach as a backstop, because every one of those
allowances is free *until a flag is wrong* — and a database created without `useFreeLimit` looks
identical to one created with it until an invoice arrives.

## What this does not do

- **It does not monitor itself.** The seeded components watch external services this project
  depends on. Monitoring yourself from inside yourself reports nothing at the moment it matters,
  and a status page that goes down with the thing it reports on is decoration. A real
  installation would run the checker somewhere the API is not.
- **There is no sign-up.** Operators are seeded from configuration; adding one is a deployment.
  An open registration form on a status page is a way for a stranger to tell your users you are
  down.
- **One operator, no roles.** There is no permission model beyond authenticated-or-not.
- **No paging, no on-call, no notifications.** It records and publishes state; it does not wake
  anybody.
- **No custom domain.** The free `*.azurestaticapps.net` hostname is deliberate.
- **The uptime bars start empty.** Ninety days of history takes ninety days.

## Running it locally

**Prerequisites:** the [.NET 10 SDK](https://dotnet.microsoft.com/download), Node 24, and Docker.

```bash
docker compose up -d --build      # SQL Server, Azurite, the API and the checker
cd client && npm ci && npm start  # the SPA on http://localhost:4200
```

The API answers on `http://localhost:5080`. The stack seeds an operator and two components, so
the page has something on it within a checker cycle rather than coming up blank:

| | |
|---|---|
| Console | <http://localhost:4200/admin> |
| Sign in | `operator@example.com` / `Statuspage-Demo-1` |

Those credentials are in `docker-compose.yml` and are deliberately worthless — the compose stack
is the only thing that has them, and it is not reachable from anywhere.

## Tests

```bash
dotnet build && dotnet test       # unit + integration, Testcontainers for SQL Server and Azurite
cd client && npm run lint && npx ng test --watch=false
cd e2e && npx playwright test     # journeys and an axe accessibility sweep
```

The integration tier starts real SQL Server and real Azurite containers rather than substituting
in-memory doubles, because the constraints being tested are database constraints. Each of them
has a test that fails when the constraint is dropped — a constraint nothing exercises is a
comment.

## Deploying

One command, and it is idempotent:

```powershell
./scripts/deploy.ps1 -ResourceGroup rg-statuspage
```

It deploys the Bicep template, generates or reuses the two secrets in Key Vault, gives the
workload identity a contained database user, runs the EF migration bundle as a one-shot job and
waits on it, builds and uploads the SPA configured for wherever it landed, and then smoke-tests
the result. Any of those failing fails the deployment.

`.github/workflows/deploy.yml` runs the same script with an OIDC federated credential. **There is
no stored secret anywhere in this repository** — no publish profile, no service principal
password, no connection string. The database is Entra-only and the identities connecting supply
their own credential, so there is no connection string with a password in it to leak.

`./scripts/teardown.ps1` removes everything and releases the SQL free-offer slot, which is one
database per subscription.

## Tech stack

| Layer | Choice |
|---|---|
| API | ASP.NET Core (.NET 10 LTS), controllers, RFC 9457 `ProblemDetails` |
| Data | EF Core, SQL Server / Azure SQL |
| Read model | Blob storage — a snapshot the public page reads directly |
| Checker | A .NET worker that runs, writes and exits |
| Front end | Angular |
| Auth | ASP.NET Core Identity + JWT, seeded operators, no public sign-up |
| Containers | Multi-stage Dockerfiles, Docker Compose, images on GHCR |
| Tests | xUnit, Testcontainers, Playwright |
| Cloud | Azure Container Apps, Static Web Apps, Key Vault, Application Insights — described in Bicep |
| CI/CD | GitHub Actions, deploying with OIDC and no stored credential |

## Roadmap

- [x] 01 — Repo skeleton, ground rules, tooling
- [x] 02 — Domain: state, hysteresis and uptime
- [x] 03 — Persistence: EF Core, SQL Server, the interval schema
- [x] 04 — Hand-written constraints, each with its proving test
- [x] 05 — The API surface: components and reads
- [x] 06 — Authentication: seeded operators, JWT, no sign-up
- [x] 07 — SSRF: what a monitored URL may be
- [x] 08 — Incidents and maintenance windows
- [x] 09 — The checker: a worker that runs and exits
- [x] 10 — The read model: config in, snapshot out
- [x] 11 — Angular: shell, tokens, and the public status page
- [x] 12 — Angular: the operator console
- [x] 13 — Containers: two images, one Compose
- [x] 14 — Tests that run in CI: integration and E2E
- [x] 15 — CI: build, test, images to GHCR
- [x] 16 — Azure, described in Bicep
- [x] 17 — The deploy pipeline, with no stored credential
- [x] 18 — Ship: observability, cost guards, README
