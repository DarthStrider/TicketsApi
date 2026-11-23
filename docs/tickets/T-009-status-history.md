# T-009 Status history and updatedAt

> Not to be implemented now. Created during T-003 to hold change tracking. The criteria below are drafts and are **not yet approved** by the product owner.

**As a** support agent, **I want to** see when a ticket last changed and how its status changed over time **so that** I can follow up on a ticket's progress.

## Draft acceptance criteria (not yet approved)
- Open question: whether to add `updatedAt`, a status history, or both.
- Given a ticket whose status changed, when I GET `/tickets/{id}`, then the response has an `updatedAt` equal to the time of the last change. A new ticket's `updatedAt` equals its `createdAt`.
- Given a ticket whose status changed several times, when I request its history, then I get every change (`from`, `to`, `at`), oldest first.
- Open question: whether a same-status PATCH (a no-op under T-003) changes `updatedAt` or adds a history entry.
- Open question: whether the history is part of the ticket body or a separate endpoint (for example GET `/tickets/{id}/history`).
- Recording "who" made a change depends on T-006 (authentication), so it can only be added after T-006.
- Adding fields to the ticket body must not break existing clients or the T-001 to T-003 tests.
