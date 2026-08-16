# statuspage

A status page for a handful of services: what is up, what is not, what happened when it was not,
and how much of the last ninety days each one has been available.

**Status:** in progress. See [Roadmap](#roadmap) below.

## Why this project

A status page is a small product with one property that makes it worth building: **the page that
reports on a system must not depend on that system.** A status page served by the API it monitors
tells you nothing at the only moment you need it.

So the public page here reads a JSON snapshot from blob storage and touches neither the API nor
the database. That is the design, and it is also — not coincidentally — what makes the whole
thing run for nothing.

Two other decisions are the substance:

- **State is stored as intervals, not samples.** Ninety days of one-minute checks is roughly
  130,000 rows per component. Ninety days of *transitions*, for a service that stayed up, is
  five. Uptime is a sum over interval durations with maintenance subtracted, not a count over
  samples.
- **A component goes down after N consecutive failures, not one.** Hysteresis is what separates
  a status page from an alarm that cries wolf, and it is a rule with a state machine behind it
  rather than an `if`.

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
- [ ] 10 — The read model: config in, snapshot out
- [ ] 11 — Angular: shell, tokens, and the public status page
- [ ] 12 — Angular: the operator console
- [ ] 13 — Containers: two images, one Compose
- [ ] 14 — Tests that run in CI: integration and E2E
- [ ] 15 — CI: build, test, images to GHCR
- [ ] 16 — Azure, described in Bicep
- [ ] 17 — The deploy pipeline, with no stored credential
- [ ] 18 — Ship: observability, cost guards, README

## Getting started

**Prerequisites:** the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Docker.

```bash
dotnet build
dotnet test
```

More once there is more to run.
