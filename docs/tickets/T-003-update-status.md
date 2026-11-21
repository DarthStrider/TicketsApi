# T-003 Change ticket status

## Acceptance criteria
- PATCH `/tickets/{id}/status` with `{ "status": "InProgress" }` returns `200`.
- The allowed transitions are Open → InProgress → Resolved → Closed, and Resolved → InProgress (reopen).
- An invalid transition returns `409 Conflict` with ProblemDetails explaining why.
- An unknown id returns `404`.
