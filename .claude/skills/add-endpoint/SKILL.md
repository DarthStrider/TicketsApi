---
name: add-endpoint
description: Adds a new Minimal API endpoint to TicketsApi following project conventions. Use when a ticket asks for a new route or a change to an existing endpoint.
---

# Adding an endpoint

1. Read the ticket in `docs/tickets/` and list its acceptance criteria.
2. Create or extend `src/TicketsApi/Features/<Feature>/<Feature>Endpoints.cs` with an extension method
   `Map<Feature>Endpoints(this IEndpointRouteBuilder app)` using `app.MapGroup("/<feature>")`.
3. Use request/response `record` types. Never expose internal entities directly.
4. Validate input. On failure, return `TypedResults.ValidationProblem(...)`.
5. Return `TypedResults` (`Ok`, `Created`, `NotFound`, `NoContent`).
6. Register it in `Program.cs`.
7. Add tests in `tests/TicketsApi.Tests/<Feature>/` using `WebApplicationFactory<Program>`:
   one test per acceptance criterion, plus a validation failure test.
8. Run `dotnet test` and report which acceptance criteria each test covers.
