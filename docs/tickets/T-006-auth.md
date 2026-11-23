# T-006 Secure the API

## Acceptance criteria
- All `/tickets` endpoints require a JWT bearer token. `/health` stays anonymous.
- Only users with the role `agent` can change status. Others get `403`.
- Before evaluating the change, PATCH `/tickets/{id}/status` runs the same read authorization as GET `/tickets/{id}`. Given a ticket the caller may not read, when they PATCH its status, then they get `404` (not `403`). Otherwise the `409` (`currentStatus`) and the same-status `200` (the full ticket) would reveal the ticket's status and contents to a caller who can't read it. (From the T-003 security audit.)
- Tests use a test authentication handler (no real identity provider needed).
- Run the `security-auditor` subagent. There must be no High or Medium findings left.
