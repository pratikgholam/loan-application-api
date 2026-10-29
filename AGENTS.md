# AGENTS.md

Guidance for AI agents working in this repository.

## Project

`loan-application-api` is a public portfolio project: a REST API for submitting and reviewing loan applications. It exists to show how the owner handles authentication, authorization, validation, data access and testing. All data is fake. The design is original and contains no code or data from any employer.

Applicants submit and track their applications. Loan officers review them and approve or reject them. Every status change is written to a history table.

## Working agreement

The owner writes the code. Agents act as pair-programming mentors: guide, review and explain. **When the owner explicitly asks the agent to write code, the agent writes it.**

- Do not write or edit production code unless the owner explicitly asks for it or says they are stuck.
- When reviewing, say directly what is wrong or weak and why.
- Do not guess at code you have not seen. Ask for the file first.
- Verify versions, package names, APIs and configuration syntax against official docs before stating them. Say when something is unverified.
- Work one stage at a time, and ask at most one question at a time.
- Every design choice must come with a reason and the alternative that was rejected.
- Do not claim in the README or repo description anything the code does not do.

## Tech stack

- .NET 10 SDK, ASP.NET Core (controllers)
- PostgreSQL
- EF Core 10 with the Npgsql provider, used with migrations for most CRUD
- Dapper with hand-written SQL, used only for the reporting endpoint
- JWT bearer authentication with role-based authorization
- xUnit for unit and integration tests
- Scalar (OpenAPI reference UI) for interactive API documentation
- Dockerfile (multi-stage: SDK → ASP.NET runtime) and full-stack compose (API + PostgreSQL with healthcheck, `.env` for secrets; no GitHub Actions — out of scope, decided 2026-10-11)

## Solution layout

```
loan-application-api/
├── src/LoanApplication.Api/        Web API, controllers, EF Core
└── tests/LoanApplication.Tests/    xUnit, references the API project
```

## Commands

Run from the repository root. `LoanApplicationApi.slnx` sits at the root, so `dotnet build` builds both projects.

```
dotnet build
dotnet test
```

Run a focused subset of tests with a filter (verified working):

```
dotnet test --filter "FullyQualifiedName~LoanTests"
```

EF Core migrations. The `dotnet-ef` tool (v10.0.12) is already installed:

```
dotnet ef migrations add <Name> --project src/LoanApplication.Api --startup-project src/LoanApplication.Api
dotnet ef database update --project src/LoanApplication.Api --startup-project src/LoanApplication.Api
```

`database update` needs a running PostgreSQL instance: start it with `docker compose up -d` (see `compose.yml` at the repo root), wait for the healthcheck, then run the command.

### Startup gotcha

`Program.cs` throws if connection string `Default` is missing. It exists only in `appsettings.Development.json` (gitignored, Development environment). Running outside Development, or on a fresh clone, fails at startup until that connection string is provided.

## Domain model

- **User**: Id, Email, PasswordHash, Role
- **Applicant**: Id, UserId, FullName, DateOfBirth (DateOnly), MonthlyIncome
- **Loan**: Id, ApplicantId, Amount, TermMonths, Purpose, Status, CreatedAt, History
- **StatusHistory**: Id, LoanId, FromStatus (nullable), ToStatus, ChangedByUserId, ChangedAt, Note
- **UserRole**: Applicant, LoanOfficer, Admin
- **LoanStatus**: Submitted=1, UnderReview=2, Approved=3, Rejected=4, Withdrawn=5

Relationships:
- User 1:0..1 Applicant
- Applicant 1:many Loan
- Loan 1:many StatusHistory
- StatusHistory.ChangedByUserId is a foreign key with no navigation property.

The `Loan.History` property is named that way because `LoanApplication` clashes with the namespace. The clash is suspected, not confirmed.

### Status transitions

Allowed transitions only:
- Submitted → UnderReview
- UnderReview → Approved
- UnderReview → Rejected (requires a non-blank reason)
- Submitted → Withdrawn
- UnderReview → Withdrawn

Any other transition must be rejected.

### Entity rules

- Setters are private. Each entity has a private EF constructor.
- Validation happens in the factory method.
- Status changes happen only through `Submit`, `StartReview`, `Approve`, `Reject` and `Withdraw`. All of them go through one private `ChangeStatus`, which checks the transition and appends a `StatusHistory` row.
- `Loan.History` returns `_history.AsReadOnly()`.
- Entities do not decide who may trigger a transition. That check belongs in controllers or services.

### Value conventions

- IDs are `Guid`.
- Money is `decimal`, stored as `numeric` with explicit precision. One currency is assumed.
- Timestamps are UTC `DateTime` values passed in by the caller, so entities stay testable. Entities must not call `DateTime.UtcNow` themselves.

## Authorization

Roles: Applicant, LoanOfficer, Admin.

| Method | Route | Access |
|---|---|---|
| POST | /auth/register | Public |
| POST | /auth/login | Public |
| POST | /applications/profile | Applicant |
| POST | /applications | Applicant |
| GET | /applications/{id} | Applicant (own), LoanOfficer, Admin |
| GET | /applications | LoanOfficer, Admin (filter by status, paged) |
| POST | /applications/{id}/start-review | LoanOfficer, Admin |
| POST | /applications/{id}/review | LoanOfficer, Admin (approve or reject) |
| POST | /applications/{id}/withdraw | Applicant (own) |
| GET | /reports/status-summary | LoanOfficer, Admin (Dapper) |

"Own" means the applicant is the owner of the loan. Enforce this in the controller or service layer.

## Data access

- EF Core handles CRUD and migrations. Entity configuration (precision, unique email, enum storage) lives in configuration classes, not in attributes on entities.
- Dapper is used only for `/reports/status-summary`, with hand-written SQL.
- Migrations are generated with `dotnet ef`. Do not edit an applied migration. Add a new one.

## Testing

- Unit tests live in `tests/LoanApplication.Tests/LoanTests.cs` and are pure in-memory: they need no database or other services.
- Unit tests cover entities and transition rules. Every status and action combination is tested, plus the UTC guard on `Submit` and every transition (42 tests, all passing).
- PostgreSQL-backed integration tests live in `tests/LoanApplication.Tests/PostgresIntegrationTests.cs` and run against a real `postgres:18-alpine` container via Testcontainers. They **require Docker**. Filter them out with `--filter "FullyQualifiedName!~PostgresIntegrationTests"` to run the no-Docker subset.
- Test names describe behavior.

## Git and repository hygiene

- `.gitignore` already covers `.idea/`, `appsettings.Development.json`, `.env`, and `bin/` and `obj/`. Keep it that way.
- Never commit secrets. JWT signing keys and connection strings with passwords come from configuration or environment variables.

## Current status

- Stage 1 (domain entities and unit tests): done. 42 xUnit tests for `Loan` pass (35 original + 7 UTC-guard tests).
- Stage 2 (EF Core): done. `AppDbContext` and entity configuration are written. The `InitialCreate` migration is generated and has been applied to a real database. `StatusHistory` → `Loan` uses `Restrict` delete behavior to protect the audit trail. The build passes.
- Stage 3 (docker-compose for PostgreSQL, apply the migration): done. `compose.yml` runs `postgres:18-alpine` with a healthcheck and a `pgdata` volume mounted at `/var/lib/postgresql` (PG18 moved `PGDATA`; mounting the parent preserves data). The `InitialCreate` migration was applied and all four tables verified. The database was cleaned back to 0 rows after verification.
- Stage 4 (authentication): done. `POST /auth/register` and `POST /auth/login` implemented with JWT bearer auth (HS256, 15-min access tokens). Roles: Applicant, LoanOfficer, Admin with policies (`ApplicantOnly`, `LoanOfficerOrAdmin`, `ApplicantOrOfficer`). 28 auth-related tests pass (hasher, token service, controller via `WebApplicationFactory` + EF InMemory). Total: 70 tests passing.
- Scalar OpenAPI UI added: `Scalar.AspNetCore` package, interactive reference at `/scalar/v1` in Development.
- Stage 5 (application endpoints): done. `POST /applications/profile`, `POST /applications`, `GET /applications/{id}`, `GET /applications`, `POST /applications/{id}/start-review`, `POST /applications/{id}/review`, `POST /applications/{id}/withdraw` implemented with full authorization and status-transition validation. 31 endpoint integration tests pass via `WebApplicationFactory` + EF InMemory (each test uses an isolated in-memory database). Ownership failures return 404 uniformly (`GetById`, `Withdraw`). Total: 101 tests passing.

A UTC guard was added to `Loan.Submit` and `ChangeStatus`: `RequireUtc` throws `ArgumentException` when the timestamp's `Kind` is not `Utc`, so bad input fails in the domain instead of at Npgsql.

- Stage 6 (Dapper reporting): done. `GET /reports/status-summary` implemented with Dapper hand-written SQL (`GROUP BY "Status"` returning count and total amount per status). `ReportsController` with `LoanOfficerOrAdmin` policy. Dapper 2.1.89 + Npgsql 10.0.3 packages added. 4 controller tests (2 happy path, 403 for Applicant, 401 anonymous) use a fake `IReportService` because EF InMemory cannot serve raw SQL. 3 `ReportService` unit tests cover the connection-string guard. Total: 99 tests passing.
  - Verified against live PostgreSQL on 2026-10-11. `StatusSummaryDto.Count` is `long`, not `int`: PostgreSQL returns `COUNT(*)` as `bigint` and Dapper's constructor matching requires an exact type match, otherwise it throws `InvalidOperationException: A parameterless default constructor or one matching signature (System.String status, System.Int64 count, System.Decimal totalamount) is required`. `SUM("Amount")` returns `numeric` and maps to `decimal` cleanly. An empty table returns zero rows, so no `NULL` sum reaches the DTO.
  - `Loan.Status` is stored as `varchar` (`LoanConfiguration.cs:16`), so the SQL groups on text values, not the enum's integers.

- Stage 7 (PostgreSQL-backed integration tests): done. Added `Testcontainers.PostgreSql` to the test project. Created `PostgresFixture` (xUnit collection fixture) that spins up `postgres:18-alpine` on a random host port, runs the real `InitialCreate` migration (not `EnsureCreated`), and provides an `AppDbContext` factory. 8 tests in `PostgresIntegrationTests.cs` verify:
  - No pending migrations remain after fixture startup, and all four tables (`Users`, `Applicants`, `Loans`, `StatusHistories`) exist
  - `Loan` save/reload `History.Count == 1`, then `StartReview` reload `== 2`
  - `ReportService.GetStatusSummaryAsync` groups correctly on live PG; `Count` is `long`, `TotalAmount` is `decimal`
  - Empty table returns zero rows
  - `StatusHistory.LoanId` FK `Restrict` blocks `DELETE` from `Loans` when history exists — asserted on exact SQLSTATE `23001` (`restrict_violation`), not merely any FK error, so `NO ACTION` would not pass
  - `Loan.Status` stored as `varchar` text ("Submitted") not integer (1)

Total: 107 tests passing (99 unit/InMemory + 8 PostgreSQL).

**Docker is now required for the full test run.** This contradicts the earlier "testing" section that says `dotnet test` needs no database or other services; that statement now holds only for the non-PostgreSQL subset. `PostgresFixture.InitializeAsync` calls `StartAsync()`, which throws when no Docker endpoint is reachable, and xUnit fails every test in the collection when a fixture throws — so a plain `dotnet test` without Docker fails rather than skipping. To run only the tests that need no Docker:

```
dotnet test --filter "FullyQualifiedName!~PostgresIntegrationTests"
```

Verified 2026-10-11: that filter passes 108/108 with no Docker. Note that overriding `DOCKER_HOST` does **not** simulate an absent Docker, because Testcontainers falls back to the default `unix:///var/run/docker.sock` context.

  - The UTC-guard cases (`Unspecified`/`Local` `DateTime` → `ArgumentException`) deliberately stay in `LoanTests.cs`, not here: they never reach the database, so they are unit tests and duplicating them in this collection only added Docker latency.
  - `PostgresFixture` reads `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` from environment (defaults `loanapp`/`loanapp`/`loanapp`), matching the `compose.yml` convention (`${POSTGRES_PASSWORD:-...}`). No credentials literals remain in source.

- Stage 8 (Dockerfile + full-stack compose): done. `Dockerfile` uses multi-stage build (`mcr.microsoft.com/dotnet/sdk:10.0` → `mcr.microsoft.com/dotnet/aspnet:10.0`), publishes API project only. `compose.yml` defines `api` (port 8080) and `db` services with `depends_on: db:condition: service_healthy`. All secrets via gitignored `.env` (`POSTGRES_PASSWORD`, `JWT_KEY`, `ASPNETCORE_ENVIRONMENT`). `.dockerignore` excludes `tests/`, build output, IDE files, and secret configs. Verified: `docker compose up --build -d` starts both services, migrations apply, full API flow works (register → login → profile → submit → get), data persists via `pgdata` volume. 108 unit/InMemory tests pass.

Remaining stages: none.

## README vs reality

The README now matches the repo (verified 2026-10-12):

- `docker compose up --build -d` runs the full stack (API + DB). `Dockerfile` exists.
- GitHub Actions entry removed (out of scope).
- Endpoint table includes `start-review`; data model uses `Loan` (not `LoanApplication`).

Trust the repo over the README. Fix the README when the code catches up (see working agreement).

## Known gaps and open items

These are unverified or unfinished. Confirm each before relying on it.

1. ~~README endpoint table missing `start-review`; old `LoanApplication` name.~~ Confirmed true; tracked above.
2. ~~Npgsql handling of `DateTime` for `timestamptz`.~~ Confirmed against live PG18: values with `Kind=Unspecified` or `Kind=Local` are rejected with `ArgumentException: Cannot write DateTime with Kind=... to PostgreSQL type 'timestamp with time zone', only UTC is supported`, surfaced as `DbUpdateException` at `SaveChangesAsync`.
3. ~~Backing fields.~~ Confirmed working by EF convention and configured in `LoanConfiguration.cs:29` via `UsePropertyAccessMode(PropertyAccessMode.Field)`: a `Loan` saved with one `History` row reloads with `History.Count == 1`, and after `StartReview` it reloads with `2`. No `HasField(...)` needed.
4. Whether `Guid.CreateVersion7()` is available in .NET 10 and whether it should replace `Guid.NewGuid()`.
5. ~~Remove the template `WeatherForecast` controller and model once real endpoints exist.~~ Removed: `WeatherForecastController.cs` and `WeatherForecast.cs` deleted; build and 42 tests pass.
6. ~~Decide whether `Loan.Submit` and the transition methods should reject `DateTime` values whose kind is not `Utc`.~~ Decided yes, and implemented: `RequireUtc` in `Loan.Submit` and `ChangeStatus` throws `ArgumentException` when `Kind != Utc`, so callers fail fast in the domain instead of at the database.
