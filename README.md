# Loan Application API

A REST API for submitting, reviewing, and tracking loan applications, built with ASP.NET Core (.NET 10) and PostgreSQL.

Applicants submit and track their applications. Loan officers review them and approve or reject. Every status change is recorded with the acting user, a timestamp, and an optional note, giving a complete audit trail.

> **Note:** This is a portfolio project. All data is fictional, and the design is original. It contains no code, data, or design patterns from any employer.

## Features

- JWT authentication with role-based authorization (Applicant, LoanOfficer, Admin)
- Server-side ownership checks: applicants can only access their own applications
- Application lifecycle with an explicit status workflow
- Append-only status history for auditing
- Paged, filterable application listing
- Status summary report (count and total amount per status)
- EF Core for writes and general reads; Dapper with hand-written SQL for reporting
- Unit and integration tests (xUnit) against a real PostgreSQL instance
- Containerized with Docker Compose; interactive API reference via Scalar

## Tech Stack

| Area | Technology |
|---|---|
| Framework | ASP.NET Core Web API (.NET 10) |
| Database | PostgreSQL |
| Data access | Entity Framework Core (Npgsql), Dapper |
| Auth | JWT bearer, role-based authorization |
| Testing | xUnit |
| Tooling | Docker, Docker Compose, Scalar |

## Application Workflow

```
Submitted ──► UnderReview ──► Approved
                    │
                    └───────► Rejected

Submitted / UnderReview ──► Withdrawn   (applicant only)
```

Each transition is stored in the `StatusHistory` table with the user, timestamp, and an optional note.

## Roles and Permissions

| Role | Permissions |
|---|---|
| **Applicant** | Register, create a profile, submit applications, view and withdraw their own applications |
| **LoanOfficer** | View all applications, start review, approve or reject, view reports |
| **Admin** | All LoanOfficer permissions |

Authorization is enforced on the server. An applicant cannot read another applicant's application, even with a valid ID.

## API Reference

| Method | Route | Access | Description |
|---|---|---|---|
| POST | `/auth/register` | Public | Create an account |
| POST | `/auth/login` | Public | Authenticate and receive a JWT |
| POST | `/applications/profile` | Applicant | Create applicant profile |
| POST | `/applications` | Applicant | Submit an application |
| GET | `/applications/{id}` | Applicant (own), LoanOfficer, Admin | Get a single application |
| GET | `/applications` | LoanOfficer, Admin | List applications (status filter, paged) |
| POST | `/applications/{id}/start-review` | LoanOfficer, Admin | Move an application to review |
| POST | `/applications/{id}/review` | LoanOfficer, Admin | Approve or reject |
| POST | `/applications/{id}/withdraw` | Applicant (own) | Withdraw an application |
| GET | `/reports/status-summary` | LoanOfficer, Admin | Count and total amount per status |

Interactive documentation is available at `/scalar/v1` when the API is running.

## Data Model

| Entity | Description |
|---|---|
| `User` | Credentials and role |
| `Applicant` | Name, date of birth, monthly income |
| `Loan` | Amount, term, purpose, current status |
| `StatusHistory` | One record per status change |

## Design Decisions

**EF Core and Dapper together.** EF Core handles most reads and writes, with migrations managing the schema. The status summary report uses Dapper with plain SQL, since an aggregate query is clearer and easier to tune as SQL than as LINQ.

**Audit trail by design.** Status changes are never overwritten; each transition adds a row to `StatusHistory`.

**Integration tests on real PostgreSQL.** Database-backed tests run against PostgreSQL rather than an in-memory substitute, so behavior matches production.

## Getting Started

### Prerequisites

- [Docker](https://docs.docker.com/get-docker/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (required for migrations and running tests)

### Run

```bash
git clone https://github.com/pratikgholam/loan-application-api.git
cd loan-application-api
docker compose up --build -d
```

The API is available at `http://localhost:8080`, with the Scalar reference at `http://localhost:8080/scalar/v1`.

### Apply database migrations

Run on first start and after any schema change, with the containers running:

```bash
dotnet tool install --global dotnet-ef   # once, if not already installed
dotnet ef database update \
  --project src/LoanApplication.Api \
  --startup-project src/LoanApplication.Api
```

### Run tests

```bash
# All tests (integration tests require Docker)
dotnet test

# Unit tests only (no Docker required)
dotnet test --filter "FullyQualifiedName!~PostgresIntegrationTests"
```

## Project Structure

```
loan-application-api/
├── src/
│   └── LoanApplication.Api/      # Web API
└── tests/
    └── LoanApplication.Tests/    # Unit and integration tests
```
