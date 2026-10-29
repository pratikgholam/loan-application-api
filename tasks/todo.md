# Task list (implementation of code-review findings)

## Phase 1: Foundation & must-fix

### Task 1: Apply migrations on API startup (IN PROGRESS)
**Description:** Add `db.Database.MigrateAsync()` on startup so `docker compose up` alone yields a working API instead of 500s until an out-of-band `dotnet ef database update`.
**Acceptance criteria:**
- [ ] `Program.cs` applies pending migrations after `app.Build()`
- [ ] Build succeeds
**Verification:** `dotnet build`
**Files:** `src/LoanApplication.Api/Program.cs`
**Dependencies:** None
**Scope:** Small (1 file)

### Task 2: Duplicate registration → Conflict (409), not 500 (IN PROGRESS)
**Description:** Wrap `SaveChangesAsync` in `Register` with `try/catch (DbUpdateException)` returning 409 so simultaneous same-email registrations don't produce an unhandled 500.
**Acceptance criteria:**
- [ ] `catch (DbUpdateException) => Conflict(...)` present in `Register`
- [ ] Build succeeds
**Verification:** `dotnet build`
**Files:** `src/LoanApplication.Api/Controllers/AuthController.cs`
**Dependencies:** Task 1 (shares Program.cs context but independent edit)
**Scope:** Small (1 file)

### Task 3: Raise password ceiling from 12 to 128 chars (IN PROGRESS)
**Description:** Change `Register_PasswordTooLong` threshold so passphrases are accepted (min 8 stays). Update the too-long unit test to use a 129-char password.
**Acceptance criteria:**
- [ ] `IsValidPassword` returns true for passwords up to 128 chars, false above
- [ ] `Register_PasswordTooLong_ReturnsBadRequest` uses a 129-char password
- [ ] Fast test suite passes
**Verification:** `dotnet test --filter "FullyQualifiedName!~PostgresIntegrationTests"`
**Files:** `src/LoanApplication.Api/Controllers/AuthController.cs`, `tests/LoanApplication.Tests/AuthControllerTests.cs`
**Dependencies:** None
**Scope:** Small (2 files)

## Checkpoint: Phase 1
- [ ] Build succeeds
- [ ] `dotnet test --filter "FullyQualifiedName!~PostgresIntegrationTests"` passes

## Phase 2: Concurrency on status writes

### Task 4: Add concurrency token to `Loan.Status` + new EF migration (IN PROGRESS)
**Description:** Mark `Loan.Status` as a concurrency token so two simultaneous conflicting status changes can't both commit. Add a new EF Core migration.
**Acceptance criteria:**
- [ ] `LoanConfiguration` sets `IsConcurrencyToken()` on `Status`
- [ ] New migration created (`dotnet ef migrations add ConcurrencyTokenOnLoanStatus`)
- [ ] Build succeeds
**Verification:** `dotnet build`, `dotnet ef migrations add`
**Files:** `src/LoanApplication.Api/Data/Configurations/LoanConfiguration.cs`, `Migrations/<timestamp>_ConcurrencyTokenOnLoanStatus.*`
**Dependencies:** Task 1
**Scope:** Small (2 files + migration)

### Task 5: Map `DbUpdateConcurrencyException` → 409 in mutating endpoints (DONE)

### Phase 3: Lean cleanups

### Task 6: Remove dead `ValidateToken` code + its tests (IN PROGRESS) (IN PROGRESS)
**Description:** In `StartReviewAsync`, `ReviewAsync`, `WithdrawAsync`, catch `DbUpdateConcurrencyException` and translate to 409 "changed since you loaded it".
**Acceptance criteria:**
- [ ] All three mutating service methods translate concurrency exceptions to a 409
- [ ] Build succeeds
**Verification:** `dotnet build`
**Files:** `src/LoanApplication.Api/Services/ApplicationService.cs`
**Dependencies:** Task 4
**Scope:** Small (1 file)

## Checkpoint: Phase 2
- [ ] Build succeeds
- [ ] Migrations apply cleanly

## Phase 3: Lean cleanups

### Task 6: Remove dead `ValidateToken` code + its tests
**Description:** `ValidateToken` (TokenService.cs + ITokenService.cs) is never called by production code; JWT bearer middleware does validation. Delete ~26 lines and its 6 tests.
**Acceptance criteria:**
- [ ] `ValidateToken` removed from `ITokenService` and `TokenService`
- [ ] `TokenServiceTests` removed
- [ ] Build + fast test suite pass
**Verification:** `dotnet build`, `dotnet test --filter "FullyQualifiedName!~PostgresIntegrationTests"`
**Files:** `src/LoanApplication.Api/Services/ITokenService.cs`, `src/LoanApplication.Api/Services/TokenService.cs`, `tests/LoanApplication.Tests/TokenServiceTests.cs`
**Dependencies:** None
**Scope:** Small (3 files)

### Task 7: Remove unused `CurrentUserService.GetRole()`
**Description:** `GetRole()` and its interface member have zero callers (controllers use `IsOfficer()`). Remove ~7 lines.
**Acceptance criteria:**
- [ ] `GetRole()` removed from `ICurrentUserService` and `CurrentUserService`
- [ ] Build succeeds
**Verification:** `dotnet build`
**Files:** `src/LoanApplication.Api/Common/CurrentUserService.cs`
**Dependencies:** None
**Scope:** Small (1 file)

### Task 8: Remove dead commented line + stale comment (DONE)

### Task 9: `Register` `Created()` + JWT key length validation (IN PROGRESS)
**Description:** Delete the commented-out `History` property line in `Loan.cs`; fix the "Find or create applicant profile" comment in `ApplicationService.cs` (no create fallback exists).
**Acceptance criteria:**
- [ ] Commented line removed from `Loan.cs`
- [ ] Stale comment corrected in `ApplicationService.cs`
- [ ] Build succeeds
**Verification:** `dotnet build`
**Files:** `src/LoanApplication.Api/Entities/Loan.cs`, `src/LoanApplication.Api/Services/ApplicationService.cs`
**Dependencies:** None
**Scope:** Small (2 files)

### Task 9: `Register` `Created()` + JWT key length validation
**Description:** `CreatedAtAction(nameof(Register))` emits a `Location` header to a POST-only route (GET 405s); return `Created()` instead. Also validate HS256 key length (≥32 bytes) at startup.
**Acceptance criteria:**
- [ ] `Register` returns `Created()` (no misleading Location header)
- [ ] Startup throws a clear error when JWT key is too short
- [ ] Build succeeds
**Verification:** `dotnet build`
**Files:** `src/LoanApplication.Api/Controllers/AuthController.cs`, `src/LoanApplication.Api/Program.cs`
**Dependencies:** Task 1 (Program.cs already edited)
**Scope:** Small (2 files)

## Checkpoint: Complete
- [ ] Full build + fast test suite pass
- [ ] Ready for review
