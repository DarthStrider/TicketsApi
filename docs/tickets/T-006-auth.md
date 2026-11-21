# T-006 Secure the API

## Acceptance criteria
- All `/tickets` endpoints require a JWT bearer token. `/health` stays anonymous.
- Only users with the role `agent` can change status. Others get `403`.
- Tests use a test authentication handler (no real identity provider needed).
- Run the `security-auditor` subagent. There must be no High or Medium findings left.
