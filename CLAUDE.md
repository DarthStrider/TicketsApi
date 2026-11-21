# TicketsApi

A small ASP.NET Core Web API for support tickets. It's a learning project for directing AI agents.

## Tech stack
- .NET 10, C#, ASP.NET Core **Minimal APIs**
- xUnit v3 + WebApplicationFactory for integration tests (pass `TestContext.Current.CancellationToken` to async calls)
- In-memory storage for now (EF Core comes in ticket T-005)

## Commands
- Build: `dotnet build`
- Test: `dotnet test`
- Format: `dotnet format`
- Run: `dotnet run --project src/TicketsApi`

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
