# ADR 0001: Assign loans to officers through RabbitMQ

- Status: Accepted
- Date: 2026-11-18
- Issue: #8

## Context

Applications were a shared pool: any loan officer could pick up any application, and nothing matched an application to an officer qualified for that kind of loan. Applicants need to choose a loan type, and each loan officer has one expertise.

## Decision

- Add a `LoanType` enum (`Personal`, `Auto`, `Home`, `Business`) to `Loan`, chosen at submission and never changed.
- Add a nullable `AssignedOfficerUserId` to `Loan` and a nullable single `Expertise` to `User`. Officer expertise management is a separate sub-task.
- The API saves the loan, then publishes a `LoanSubmitted` event (loan id + loan type) to a durable RabbitMQ queue. Publishing uses publisher confirms, so a publish counts only when the broker confirms it. Up to four attempts are made; after that the error is logged and the loan stays saved but unassigned. The submit response still succeeds.
- A separate worker project consumes one message at a time (`prefetch 1`), finds a `LoanOfficer` whose expertise matches, assigns the officer, and only then acknowledges. A failed assignment is nacked and redelivered. No matching officer is logged and acknowledged; the loan stays unassigned.
- Assignment is idempotent: assigning the same officer twice is a no-op, and the worker skips loans that already have an officer.
- Officers see only applications assigned to them; admins see all. The application detail response includes the assigned officer's user id.
- RabbitMQ runs in `compose.yml` with credentials from the gitignored `.env`, a healthcheck, and `api`/`worker` waiting on it.

## Alternatives rejected

- **Transactional outbox.** Stronger delivery guarantee, but adds a table, a relay, and duplicate handling. The accepted gap is small: a loan can stay unassigned if publishing exhausts its retries or the API crashes between save and publish. A future periodic sweep or the outbox can close it.
- **Eventing framework (MassTransit and similar).** Removes boilerplate but hides the confirm/ack semantics this project exists to show.
- **Competing consumers or prefetch > 1.** Faster, but assignment order and the single-writer assumption would need locking. The assignment rate does not justify it.
- **Junction table for expertise.** Multiple expertise types per officer is explicitly out of scope; one nullable column is enough.
- **Worker changing loan status.** Assignment is routing, not review. Status transitions and history stay untouched.

## Consequences

- One worker instance. A second instance would need assignment locking.
- The worker uses a separate process but the same API assembly for the DbContext, entities, and event contract.
- Existing loans are backfilled with `LoanType = 'Personal'` by the migration's column default.
- Integration tests need Docker and pull `rabbitmq:4-management`; the fast test subset excludes both integration classes with `--filter "FullyQualifiedName!~IntegrationTests"`.
