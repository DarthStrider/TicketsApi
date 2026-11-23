# TicketsApi

A small ASP.NET Core Web API for support tickets. It's a learning project for directing AI agents.

## Tech stack
- .NET 10, C#, ASP.NET Core **Minimal APIs**
- xUnit v3 + WebApplicationFactory for integration tests (pass `TestContext.Current.CancellationToken` to async calls)
- SQLite through EF Core (since T-005). The connection string is `ConnectionStrings:Tickets`; Development uses
  `App_Data/tickets.dev.db` and migrates at startup. Every other environment (Production, Staging, ...) must have
  its migrations applied before it starts, or startup fails.
- Integration tests use `TicketsApiFactory`, which gives every test host its own temp SQLite file.

## Commands
- Build: `dotnet build`
- Test: `dotnet test`
- Format: `dotnet format`
- Run: `dotnet run --project src/TicketsApi`
- Restore local tools (once): `dotnet tool restore`
- Add a migration: `dotnet ef migrations add <Name> --project src/TicketsApi --output-dir Data/Migrations`
- Apply migrations (Production): `dotnet ef database update --project src/TicketsApi --connection "<connection string>"`

## Structure
- `src/TicketsApi/` has the API. Group endpoints by feature in `Features/<Feature>/` (vertical slices).
- `tests/TicketsApi.Tests/` has the tests, and the folders mirror the feature folders.
- `docs/tickets/` holds the work items with their acceptance criteria. Read the ticket before starting.

## Conventions
- Return `TypedResults` from endpoints, and use `ProblemDetails` for errors.
- Validate input at the endpoint boundary. Never trust client input.
- Use async all the way down. Never use `.Result` or `.Wait()`.
- Inject dependencies through the constructor or the endpoint parameters. No static service locators.
- Every endpoint needs integration tests covering the happy path and the validation failure.

## Workflow rules
- Before writing code for a ticket, explore and then present a plan. Wait for approval.
- Write or update tests first, and confirm they fail before implementing.
- Never weaken or delete a test to make it pass. If a test looks wrong, stop and ask.
- Keep changes scoped to the ticket. Use one branch per ticket: `feature/T-00X-short-name`.
- Ticket flow: `/start-ticket T-00X` -> work -> `/create-pr` (opens a pull request) -> the user merges it on GitHub -> `/finish-ticket` (switch to main, pull, delete the branch).
- Never merge pull requests. The user reviews and merges them on GitHub.

## Team roles (subagents in .claude/agents)
- **Developer:** the main session. Writes production code in `src/`.
- **product-manager:** owns `docs/tickets/`. Checks each ticket at the start and records every requirement change.
- **qa-tester:** owns `tests/`. Maps every acceptance criterion to tests, checks the tests can fail, and signs off with PASS/FAIL.
- **code-reviewer** and **security-auditor:** read-only reviews.
- **teacher:** owns `docs/learning/`. Records lessons for the user after each ticket, or when the user says "teacher, note this".
- The user is the product owner and makes every product decision. Agents propose, the user decides.

## Change requests
- When the user asks for new or changed behaviour, **first** have the `product-manager` update the ticket's acceptance criteria
  and show the diff. Then the tests, then the code. Anything out of scope goes into a new ticket, not into the current one.
- Never add behaviour that isn't in the ticket. Suggest it instead.
- Commits are blocked by a hook unless `dotnet build` and `dotnet test` pass.
- When finished, run `dotnet build` and `dotnet test`, then ask the `code-reviewer` subagent to review the diff.

## Growing the solution
- The solution file is `TicketsApi.slnx`. New projects go under `src/` or `tests/` and are added with `dotnet sln add`.
- Shared build settings live in `Directory.Build.props`. Don't repeat them in .csproj files.
- NuGet versions live only in `Directory.Packages.props`. Use `<PackageReference Include="X" />` with no version.

## Dependencies
- Before using an API from a package, make sure the package is referenced. Versions go only in `Directory.Packages.props`.
- Never write a package version from memory. Check nuget.org for the latest stable version and any deprecation notice, and say what you checked.
- Keep `Microsoft.AspNetCore.*` packages on the same major version as the target framework (net10.0 -> 10.0.x).
- For a major-version upgrade, read the official migration guide and list every required change (csproj, global.json, CI) before editing.
- Tests run on Microsoft.Testing.Platform (opted in via `global.json`). Don't remove that file or switch back to VSTest.

## Verification
- After any change, run `dotnet build` and `dotnet test` and show the output.
- If you cannot run them, say so explicitly. Never claim the work is done.
