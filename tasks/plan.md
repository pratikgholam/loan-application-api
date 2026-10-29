# Implementation Plan: Address code-review findings

## Overview
Implement the must-fix and nice-to-have findings from the code review: migrate-on-startup, duplicate-registration race, password ceiling, concurrency protection on status writes, and removal of dead code.

## Architecture Decisions
- Migrate-on-startup is additive (only runs when needed) and mirrors `PostgresFixture.InitializeAsync` logic already proven in tests.
- Concurrency protection uses `IsConcurrencyToken()` on `Loan.Status` — EF Core rejects stale saves, mapped to 409 in mutating endpoints.
- Duplicate registration is guarded with `try/catch (DbUpdateException)` around `SaveChangesAsync`, consistent with existing `InvalidOperationException` handling.
- No new dependencies; every fix uses existing patterns.

## Task List

### Phase 1: Foundation & must-fix (data-safety)
- [ ] Task 1: Apply migrations on API startup (`Program.cs`)
- [ ] Task 2: Duplicate registration → Conflict (409), not 500 (`AuthController.cs`)
- [ ] Task 3: Raise password ceiling from 12 to 128 chars (+ test update)

### Checkpoint: Phase 1
- [ ] Build succeeds, `dotnet test --filter "FullyQualifiedName!~PostgresIntegrationTests"` passes

### Phase 2: Concurrency on status writes
- [ ] Task 4: Add concurrency token to `Loan.Status` + new EF migration
- [ ] Task 5: Map `DbUpdateConcurrencyException` → 409 in StartReview/Review/Withdraw

### Checkpoint: Phase 2
- [ ] Build succeeds, migrations apply cleanly

### Phase 3: Lean cleanups (dead code)
- [ ] Task 6: Remove dead `ValidateToken` code + its tests
- [ ] Task 7: Remove unused `CurrentUserService.GetRole()` (+ interface member)
- [ ] Task 8: Remove dead commented line in `Loan.cs` + stale comment in `ApplicationService.cs`
- [ ] Task 9: `Register` returns `Created()` instead of `CreatedAtAction` to a POST route + JWT key length validation at startup

### Checkpoint: Complete
- [ ] Full build + fast test suite pass
- [ ] Ready for review

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| Docker unavailable → can't run PostgreSQL-backed tests | Med | Rely on build + InMemory unit tests; note which findings need live PG to verify |
| New migration needed for concurrency token | Low | Add migration, verify with `dotnet ef migrations add` then build |

## Open Questions
- None; all changes are small, pattern-consistent edits.
