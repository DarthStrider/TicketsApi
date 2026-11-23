# T-005 Persist with EF Core + SQLite

## Acceptance criteria
- Replace in-memory storage with EF Core (SQLite) behind the existing endpoints. No API contract changes.
- All existing tests still pass unchanged (this is your regression suite).
- Migrations are committed. The connection string comes from configuration, not code.
- The T-003 transition rules and the atomic check-and-set guarantee (T-003 criterion 24) still hold with EF Core. A concurrency token (for example a row version, or the status as the concurrency token) makes a change that loses to a concurrent change be re-evaluated as in T-003 criterion 25, never a lost update. The T-003 criterion 24 and 25 race tests are kept: the EF Core store reuses the shared internal singleton `TicketStoreTestHooks` (calling it between the read and the save; it does nothing by default and cannot be reached over HTTP), so the same race tests run against the real database.
- Run the `security-auditor` subagent and fix every High finding.
