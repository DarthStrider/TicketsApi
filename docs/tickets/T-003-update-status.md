# T-003 Change ticket status

## Acceptance criteria
- PATCH `/tickets/{id}/status` with `{ "status": "InProgress" }` returns `200`.
- The allowed transitions are Open → InProgress → Resolved → Closed, and Resolved → InProgress (reopen).
- An invalid transition returns `409 Conflict` with ProblemDetails explaining why.
- An unknown id returns `404`.
- Given tickets with different statuses, when I GET `/tickets?status=<status>`, then I get only the tickets with that status. This is the exclusion test deferred from T-002 criterion 9.
