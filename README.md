# Loan Application API

A REST API for submitting and reviewing loan applications, built with ASP.NET Core and PostgreSQL.

Applicants apply for a loan and track their application. Loan officers review applications and approve or reject them. Every status change is recorded, so there is always a full history of who did what and when.

> All data in this project is fake. The design is my own and contains no code or data from any employer.

## Why I built this

I'm a backend engineer with 5+ years in C#, .NET Core and SQL Server. Most of my work has been on proprietary systems, so I can't show the code. One of those systems was the mobile API layer of a loan platform, where I built around 35 endpoints covering intake, approval and status tracking.

This project is my public version of that kind of work, rebuilt from scratch with fake data and modern tooling. I wanted a place where someone can read the code and see how I approach authentication, authorization, validation, data access and testing, instead of taking my word for it.

## How it works

### The workflow

An application moves through these statuses:

```
Submitted → UnderReview → Approved
                        → Rejected
Submitted / UnderReview → Withdrawn (by the applicant)
```

Each transition is written to a status history table with the user who made the change, the time, and an optional note.

### Roles

| Role | Can do |
|---|---|
| Applicant | Register, submit applications, view and withdraw their own applications |
| LoanOfficer | View all applications, review them (approve or reject), see reports |
| Admin | Everything a loan officer can do, plus reports |

Users log in with email and password and receive a JWT. Role checks are enforced on the server, so an applicant cannot read someone else's application even if they know its ID.

### Endpoints

| Method | Route | Access | Description |
|---|---|---|---|
| POST | `/auth/register` | Public | Create an account |
| POST | `/auth/login` | Public | Returns a JWT |
| POST | `/applications` | Applicant | Submit an application |
| GET | `/applications/{id}` | Applicant (own), LoanOfficer | Get one application |
| GET | `/applications` | LoanOfficer | List applications, filter by status, paged |
| POST | `/applications/{id}/review` | LoanOfficer | Approve or reject |
| POST | `/applications/{id}/withdraw` | Applicant (own) | Withdraw an application |
| GET | `/reports/status-summary` | LoanOfficer, Admin | Count and total amount per status |

### Data model

| Entity | Purpose |
|---|---|
| `User` | Login credentials and role |
| `Applicant` | The person applying: name, date of birth, monthly income |
| `LoanApplication` | Amount, term, purpose, current status |
| `StatusHistory` | One row per status change |

### Data access

The API uses **EF Core** for most reads and writes, with migrations managing the schema. The reporting endpoint uses **Dapper** with hand-written SQL, because an aggregate query is clearer and easier to tune as plain SQL than as LINQ.

## Tech stack

- ASP.NET Core Web API (.NET 10)
- PostgreSQL
- Entity Framework Core (Npgsql provider) and Dapper
- JWT bearer authentication with role-based authorization
- xUnit for unit and integration tests
- Docker and docker-compose
- GitHub Actions for CI

## Running it locally

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Docker.

```bash
git clone https://github.com/pratikgholam/loan-application-api.git
cd loan-application-api
docker compose up
```

Run the tests with:

```bash
dotnet test
```

## Project structure

```
loan-application-api/
├── src/
│   └── LoanApplication.Api/       # the Web API
└── tests/
    └── LoanApplication.Tests/     # unit and integration tests
```


### PS:- This project is not currently completed yet and I am still working on it.
