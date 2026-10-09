# AGENTS.md

Guidance for AI agents working in this repository.

## Project

`loan-application-api` is a public portfolio project: a REST API for submitting and reviewing loan applications. It exists to show how the owner handles authentication, authorization, validation, data access and testing. All data is fake. The design is original and contains no code or data from any employer.

Applicants submit and track their applications. Loan officers review them and approve or reject them. Every status change is written to a history table.

## Working agreement

The owner writes the code. Agents act as pair-programming mentors: guide, review and explain.

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
- Planned: Dockerfile and full-stack compose (database compose exists), GitHub Actions CI

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
| POST | /applications | Applicant |
| GET | /applications/{id} | Applicant (own), LoanOfficer |
| GET | /applications | LoanOfficer (filter by status, paged) |
| POST | /applications/{id}/start-review | LoanOfficer |
| POST | /applications/{id}/review | LoanOfficer (approve or reject) |
| POST | /applications/{id}/withdraw | Applicant (own) |
| GET | /reports/status-summary | LoanOfficer, Admin (Dapper) |

"Own" means the applicant is the owner of the loan. Enforce this in the controller or service layer.

## Data access

- EF Core handles CRUD and migrations. Entity configuration (precision, unique email, enum storage) lives in configuration classes, not in attributes on entities.
- Dapper is used only for `/reports/status-summary`, with hand-written SQL.
- Migrations are generated with `dotnet ef`. Do not edit an applied migration. Add a new one.

## Testing

- Unit tests live in `tests/LoanApplication.Tests/LoanTests.cs` and are pure in-memory: `dotnet test` requires no database or other services.
- Unit tests cover entities and transition rules. Every status and action combination is tested, plus the UTC guard on `Submit` and every transition (42 tests, all passing).
- Integration tests are planned for later stages and will run against PostgreSQL.
- Test names describe behavior.

## Git and repository hygiene

- `.gitignore` already covers `.idea/`, `appsettings.Development.json`, `.env`, and `bin/` and `obj/`. Keep it that way.
- Never commit secrets. JWT signing keys and connection strings with passwords come from configuration or environment variables.

## Current status

- Stage 1 (domain entities and unit tests): done. 42 xUnit tests for `Loan` pass (35 original + 7 UTC-guard tests).
- Stage 2 (EF Core): done. `AppDbContext` and entity configuration are written. The `InitialCreate` migration is generated and has been applied to a real database. `StatusHistory` → `Loan` uses `Restrict` delete behavior to protect the audit trail. The build passes.
- Stage 3 (docker-compose for PostgreSQL, apply the migration): done. `compose.yml` runs `postgres:18-alpine` with a healthcheck and a `pgdata` volume mounted at `/var/lib/postgresql` (PG18 moved `PGDATA`; mounting the parent preserves data). The `InitialCreate` migration was applied and all four tables verified. The database was cleaned back to 0 rows after verification.
- Stage 4 (authentication): done. `POST /auth/register` and `POST /auth/login` implemented with JWT bearer auth (HS256, 15-min access tokens). Roles: Applicant, LoanOfficer, Admin with policies (`ApplicantOnly`, `LoanOfficerOnly`, `AdminOnly`, `LoanOfficerOrAdmin`). 28 auth-related tests pass (hasher, token service, controller via `WebApplicationFactory` + EF InMemory). Total: 70 tests passing.
- Scalar OpenAPI UI added: `Scalar.AspNetCore` package, interactive reference at `/scalar/v1` in Development.

A UTC guard was added to `Loan.Submit` and `ChangeStatus`: `RequireUtc` throws `ArgumentException` when the timestamp's `Kind` is not `Utc`, so bad input fails in the domain instead of at Npgsql.

Remaining stages: application endpoints, Dapper reporting, integration tests, Dockerfile and full-stack compose, GitHub Actions CI.

## README vs reality

The README is aspirational. Verified against the repo as of 2026-10-08:

- It documents `docker compose up` as the way to run the whole stack, but there is no `Dockerfile` and `compose.yml` only runs the database (no API service yet).
- Its tech stack lists Docker and GitHub Actions, but there is no `.github/workflows/` directory.
- Its endpoint table is missing `start-review`, and its data model table still uses the old `LoanApplication` name instead of `Loan`.

Trust the repo over the README. Fix the README when the code catches up (see working agreement).

## Known gaps and open items

These are unverified or unfinished. Confirm each before relying on it.

1. ~~README endpoint table missing `start-review`; old `LoanApplication` name.~~ Confirmed true; tracked above.
2. ~~Npgsql handling of `DateTime` for `timestamptz`.~~ Confirmed against live PG18: values with `Kind=Unspecified` or `Kind=Local` are rejected with `ArgumentException: Cannot write DateTime with Kind=... to PostgreSQL type 'timestamp with time zone', only UTC is supported`, surfaced as `DbUpdateException` at `SaveChangesAsync`.
3. ~~Backing fields.~~ Confirmed working by EF convention: a `Loan` saved with one `History` row reloads with `History.Count == 1`, and after `StartReview` it reloads with `2`. No `HasField(...)` needed.
4. Whether `Guid.CreateVersion7()` is available in .NET 10 and whether it should replace `Guid.NewGuid()`.
5. ~~Remove the template `WeatherForecast` controller and model once real endpoints exist.~~ Removed: `WeatherForecastController.cs` and `WeatherForecast.cs` deleted; build and 42 tests pass.
6. ~~Decide whether `Loan.Submit` and the transition methods should reject `DateTime` values whose kind is not `Utc`.~~ Decided yes, and implemented: `RequireUtc` in `Loan.Submit` and `ChangeStatus` throws `ArgumentException` when `Kind != Utc`, so callers fail fast in the domain instead of at the database.
