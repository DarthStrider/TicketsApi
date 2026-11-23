# T-005 Persist with EF Core + SQLite

**As an** operator, **I want** tickets stored in a SQLite database through EF Core **so that** they survive a restart of the API, with no change for clients.

## Acceptance criteria

### Storage
1. `ITicketStore` is implemented with EF Core on SQLite and registered as `ITicketStore`. Its four method signatures are unchanged, so tests that replace it with `RemoveAll<ITicketStore>()` still work. `InMemoryTicketStore` is deleted.
2. Given a ticket was created and its status changed, when the API is stopped and started again with the same connection string, then GET `/tickets/{id}` returns the same body (`id`, `title`, `description`, `status`, `createdAt`). The test uses two hosts in sequence on the same temp file.
3. Given a ticket created with any valid title and description (200 and 4000 UTF-16 code units, emoji, accents, a `null` or an empty description), when I GET it or list it, then `title` and `description` are returned exactly as created, with no truncation or normalization.
   - The model declares `HasMaxLength(200)` for `title` and `HasMaxLength(4000)` for `description` as documentation only. SQLite does not enforce them, and the endpoint stays the only validator (T-001). There are no CHECK constraints.
4. Given a ticket created at a `createdAt` with sub-millisecond precision, when I GET it or list it, then `createdAt` is identical to the POST `201` value, to the 100 ns tick, with offset `+00:00`.

### Order and filter (T-002 criteria 4, 5, 9 to 11; T-003 criterion 26)
5. Given tickets with different `createdAt`, when I GET `/tickets`, then they are returned newest first, and the database does the sorting (not in memory).
   - `createdAt` is stored as UTC ticks (an INTEGER) through a value converter and read back with offset 0.
6. Given tickets with the same `createdAt`, when I GET `/tickets`, then ties are ordered by `id` descending, exactly as `Guid.CompareTo` orders them.
   - `id` is stored as uppercase TEXT (EF Core's SQLite default), which sorts the same as `Guid.CompareTo`. It is not stored as a BLOB.
7. Given `?status=` in any case, when I GET `/tickets`, then the results are the same as before T-005 (T-002 criterion 11, T-003 criterion 26).
   - The status is stored as its canonical name in a TEXT column with a max length, not as a number.

### Status change and concurrency (T-003 criteria 24 and 25)
8. Each ticket has an integer version that is incremented on every status write. It is the concurrency token, and the status is not. Given a status change that read version N, when the ticket was written in the meantime (even if its status is back to the one that was read), then the write fails and is re-evaluated as in T-003 criterion 25. It is never a lost update.
9. The EF Core store calls the shared internal singleton `TicketStoreTestHooks.BeforeStatusWriteAsync` after it reads the ticket and before it writes. No transaction or lock is held while the hook runs. The hook does nothing by default and can't be reached over HTTP. All the tests in `ChangeStatusConcurrencyTests` pass against SQLite with their assertions unchanged.
10. The version never appears in any response body or header. The API contract is unchanged.

### Configuration and migrations
11. The connection string is read from configuration under `ConnectionStrings:Tickets` (`GetConnectionString("Tickets")`). No connection string appears in C# code.
    - Development: `appsettings.Development.json` points to `App_Data/tickets.dev.db`.
    - Production: there is no default. The value must come from the environment (`ConnectionStrings__Tickets`).
    - `*.db`, `*.db-wal` and `*.db-shm` are in `.gitignore`, and no database file is committed.
12. Given no `ConnectionStrings:Tickets` value, when the API starts, then startup fails with an error that names the missing key. It never falls back to a default file.
13. Migrations are committed under `src/TicketsApi/`. An initial migration creates the schema.
    - Packages: `Microsoft.EntityFrameworkCore.Sqlite` and `Microsoft.EntityFrameworkCore.Design` at `10.0.15`, with versions only in `Directory.Packages.props`. `dotnet-ef` `10.0.15` is a local tool (`.config/dotnet-tools.json`).
    - The developer checked nuget.org: 10.0.15 is the latest stable 10.0.x, 11.0.0 targets net11 only, and no deprecation or vulnerability notice was shown.
14. Given the committed migrations, when a test asks EF Core whether the model has pending changes, then there are none.
15. Given an empty database, when the API starts in Development, then all migrations are applied before the first request is served. The test hosts apply the migrations to their own database in every environment they run in, including Production. In any environment other than Development, the API does not apply migrations at startup. They are applied before deploy with `dotnet ef database update` or a migration bundle.

### Tests and isolation
16. Every existing test passes. No test is deleted, skipped or weakened, and no assertion changes. Only the test host setup may change, so that each host gets its own empty database: a temp file with a unique name. Given two test hosts running in parallel, when one creates tickets, then the other does not see them.
    - Test hosts are `IDisposable`, and tests create them with `using`. Disposing a host deletes its database file and any `-wal` and `-shm` files.
    - Given a host created inside a test (for example with `WithWebHostBuilder` or `WithClock`), when that host is disposed at the end of the test, then its database file, `-wal` and `-shm` files no longer exist. They are deleted when that host is disposed, not later (not when the class fixture is disposed).
    - Given the shared class-fixture host, when the fixture is disposed at the end of the test class, then its database file, `-wal` and `-shm` files no longer exist.
    - A database file chosen by the test (`FixedDatabaseFactory`) is not deleted by the host. The test that chose it deletes it, so that it can be reused by a second host (criterion 2).
    - Approved exception (product owner): the over-the-cap `413` tests in `ErrorsAndLimitsTests.cs`, `ChangeStatusLimitsTests.cs` and `Localization/LocalizedLimitsTests.cs` also changed how they send that request. They now use the shared helper `tests/TicketsApi.Tests/Tickets/OverCapRequests.cs`, which sends `Expect: 100-continue` with a 30 s wait, so Kestrel rejects on `Content-Length` before any body is sent. Only the send call changed; no assertion changed. Reason: without it these tests were flaky, because Kestrel answered while the client was still uploading and the client sometimes saw a connection reset instead of the `413` (under load with a 4 MB body: 352/600 failures without the helper, 0/600 with it). Real clients that don't send `Expect: 100-continue` can still see a reset on very large bodies; that is a possible future ticket, not part of T-005.
17. The integration tests use real SQLite, not the EF Core InMemory provider and not an in-memory store.

### Errors
18. Given the database fails (it can't be opened, or a write throws), when a request reaches the store, then the response is the existing `500` ProblemDetails with the localized `Title500` (T-004 criteria 9 and 10), in Development and Production. It has no exception details, SQL or file path. There is no new status code.
    - Optional: a variant of the `500` test with a real broken database (for example an unwritable path), in addition to the existing `ThrowingTicketStore` test.
19. A concurrency conflict is never returned as `500`. It always ends as `200` or `409`, as in T-003 criterion 25.
20. Given a database with migrations that have not been applied, when the API starts in any environment other than Development (for example Production or Staging), then startup fails with a clear error saying that migrations are pending. It never serves requests against an out-of-date schema.

### Security
21. Run the `security-auditor` subagent and fix every High finding. The audit also checks these points, and each finding is fixed or recorded in a new ticket:
    - No `FromSqlRaw` or `ExecuteSqlRaw` with SQL built from strings. Only LINQ, `ExecuteUpdate`, or the parameterized/interpolated APIs are used.
    - `EnableSensitiveDataLogging` and `EnableDetailedErrors` are never turned on, in any environment.
    - The database file is outside `wwwroot` and every served path, and is not committed. File permissions are the deployer's job: they are documented, not tested.
    - SQLite needs no secret in the connection string. If Production moves to a server database later, its credentials come from environment variables or user-secrets, never from `appsettings.json`.

## Out of scope

Not to be implemented in T-005.

- Paging in SQL: T-008. Criterion 5 lets the database sort, so T-008 can page in SQL.
- `updatedAt` and status history: T-009 (a later migration).
- Ticket links table: T-010 (planned after T-005).
- Possible new tickets, not requested yet:
  - `503` with `Retry-After` for a busy or unavailable database
  - a database check in `/health`
  - database backups
  - a server database (for example PostgreSQL) for multi-instance Production
  - exposing the version as `ETag` / `If-Match`
