# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2026-11-18

### Added

- Loan type chosen at submission (`Personal`, `Auto`, `Home`, `Business`), stored on `Loan` and immutable thereafter.
- Automatic assignment of a submitted loan to the loan officer whose `Expertise` matches the loan type: the API publishes a `LoanSubmitted` message to RabbitMQ and a separate worker (`src/LoanApplication.Worker`) consumes it, assigns the officer, and acknowledges only after the assignment is saved.
- `AssignedOfficerUserId` (nullable) on `Loan` and `Expertise` (nullable `LoanType`) on `User`.
- Publisher confirms plus a bounded retry (up to four attempts) on publish; publish failures are logged and never fail the submit request. Unassigned loans stay saved for manual or future-sweep handling.
- Worker behaviour: prefetch 1, manual ack after save, nacked/redelivered on failure, unmatched loans logged and acked, idempotent assignment, loans that already have an officer skipped.
- Officers' application list filtered to their own assignments; admins see all; the detail response includes the assigned officer id.
- RabbitMQ (`rabbitmq:4-management`) in `compose.yml` with a healthcheck and credentials from `.env`; worker container alongside the API.
- Integration test against a real RabbitMQ container (`RabbitMqIntegrationTests`), in addition to the existing PostgreSQL tests.
- ADR `docs/adr/0001-rabbitmq-loan-assignment.md`.
- Concurrency token on `Loan.Status`: conflicting status writes cannot both commit and surface as HTTP 409.
- Migrations applied automatically on API startup.
- JWT signing key length validation (at least 32 bytes) at startup.
- This changelog.

### Changed

- `POST /applications` now requires a `loanType`.
- `GET /applications` for loan officers returns only their assignments.
- `POST /auth/register` returns `201 Created` without a misleading `Location` header.
- Password length ceiling raised from 12 to 128 (minimum 8 unchanged).
- Duplicate registration race handled as 409 instead of 500.
- Test suite: 122 tests total (113 no-Docker + 8 PostgreSQL + 1 RabbitMQ).

### Removed

- Dead code: `TokenService.ValidateToken`, `CurrentUserService.GetRole`, a commented-out `History` property line in `Loan`, and a stale comment in `ApplicationService`.

## [1.0.0] - 2026-10-12

### Added

- Initial REST API: registration and login with JWT, applicant profiles, loan applications with an explicit status workflow and append-only status history, paged and filterable listing, and a status summary report via Dapper.
- EF Core with Npgsql and migrations; PostgreSQL via Docker Compose.
- xUnit unit and integration tests, including PostgreSQL via Testcontainers.
- Multi-stage Dockerfile and full-stack compose; Scalar OpenAPI reference UI.
- Interactive API documentation at `/scalar/v1`.