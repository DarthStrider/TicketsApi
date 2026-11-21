# T-005 Persist with EF Core + SQLite

## Acceptance criteria
- Replace in-memory storage with EF Core (SQLite) behind the existing endpoints. No API contract changes.
- All existing tests still pass unchanged (this is your regression suite).
- Migrations are committed. The connection string comes from configuration, not code.
- Run the `security-auditor` subagent and fix every High finding.
