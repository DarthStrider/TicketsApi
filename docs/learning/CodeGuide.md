# Code Guide: src/TicketsApi

A walk through every class and every function in the API, written for a developer who is learning ASP.NET Core and this codebase. Read it next to the code: each section links to its source file. Ticket and criterion numbers (for example "T-003 criterion 25") come from the comments in the code and point to `docs/tickets/`.

## How a request flows

```
HTTP request
  -> Kestrel (the web server; enforces the request body size limit while the body is read)
  -> middleware, in the order Program.cs adds it:
       1. request localization   (picks English or French from Accept-Language)
       2. Content-Language/Vary  (added to every response when it starts)
       3. exception handler      (turns an unhandled exception into a ProblemDetails response)
       4. status code pages      (turns an empty 4xx/5xx response into a ProblemDetails response)
       5. authentication         (reads the Authorization: Bearer token and validates the JWT)
       6. authorization          (fallback policy: a signed-in user is required unless the endpoint
                                  is AllowAnonymous; PATCH also needs the Agent policy)
  -> routing picks an endpoint (/health or /tickets/...)
  -> endpoint handler in TicketsEndpoints  (validates input, checks read access with ITicketAccess,
                                            applies the rules)
  -> ITicketStore, implemented by EfTicketStore  (one per request)
  -> TicketsDbContext  (EF Core, one per request)
  -> SQLite file (App_Data/tickets.dev.db in Development)
```

Before the app accepts any request, `Program.cs` calls `ValidateAuthSettings` (outside Development the identity provider's authority and audience must be configured) and `InitializeTicketsDatabaseAsync`, which checks the connection string and the database schema. If either fails, the app doesn't start.

**Authentication and authorization (T-006).** They run after the error handlers, so a 401 or 403 they produce is turned into a localized `ProblemDetails` like every other error. The **fallback policy** applies to every request whose endpoint doesn't set its own authorization, including requests that match no route, so without a token even `/nothing` or `/tickets/abc` gets 401, not 404 (criterion 1). Only `/health` and, in Development, the OpenAPI document opt out with `AllowAnonymous()`. A caller who isn't an agent sees only the tickets they created; someone else's ticket gives the same 404 as an unknown id.

The response goes back out through the same middleware in reverse. Every error, whichever layer produces it, ends up as a `ProblemDetails` JSON body (RFC 9457) with a title in the request's language.

## Contents

1. [Program.cs](#programcs): startup, services and middleware
2. [Auth/AuthSetup.cs](#authauthsetupcs): JWT bearer authentication, the fallback and Agent policies
3. [Auth/AgentAuthorizationHandler.cs](#authagentauthorizationhandlercs): the Agent policy's requirement and handler
4. [Auth/ClaimsPrincipalExtensions.cs](#authclaimsprincipalextensionscs): the caller's `sub`
5. [Features/Tickets/TicketsEndpoints.cs](#featuresticketsticketsendpointscs): the HTTP endpoints
6. [Features/Tickets/ITicketAccess.cs](#featuresticketsiticketaccesscs): who may do what with tickets
7. [Features/Tickets/TicketAccess.cs](#featuresticketsticketaccesscs): the implementation of the access rules
8. [Features/Tickets/Ticket.cs](#featuresticketsticketcs): `TicketStatus` and `Ticket`
9. [Features/Tickets/TicketContracts.cs](#featuresticketsticketcontractscs): request and response records
10. [Features/Tickets/TicketTransitions.cs](#featuresticketstickettransitionscs): allowed status changes
11. [Features/Tickets/ITicketStore.cs](#featuresticketsiticketstorecs): the storage interface
12. [Features/Tickets/EfTicketStore.cs](#featuresticketsefticketstorecs): the EF Core implementation
13. [Features/Tickets/TicketStoreTestHooks.cs](#featuresticketsticketstoretesthookscs): a test-only seam
14. [Data/TicketsDbContext.cs](#dataticketsdbcontextcs): the EF Core model
15. [Data/TicketsDatabase.cs](#dataticketsdatabasecs): connection string and startup checks
16. [Data/DesignTimeTicketsDbContextFactory.cs](#datadesigntimeticketsdbcontextfactorycs): for `dotnet ef`
17. [Data/Migrations](#datamigrations): generated schema history
18. [Localization/LocalizationSetup.cs](#localizationlocalizationsetupcs): English and French
19. [Localization/Messages.cs](#localizationmessagescs): the resource marker
20. [TicketsApi.csproj](#ticketsapicsproj): packages and the user-secrets id
21. [appsettings.json and appsettings.Development.json](#appsettingsjson-and-appsettingsdevelopmentjson): configuration

---

## Program.cs

Source: [Program.cs](../../src/TicketsApi/Program.cs)

The entry point. It uses **top-level statements**: there is no `Main` method written out; the compiler generates one from the statements in this file. It does three things in order: register services, build the app and check the auth settings and the database, then set up the middleware pipeline and endpoints.

### Service registration (lines 6 to 15)

```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
```

`CreateBuilder` loads configuration (appsettings files, environment variables, command-line args), logging and the dependency injection (DI) container. Then each `builder.Services.Add...` line registers something the app can ask for later:

| Line | What it registers | Lifetime and why |
|---|---|---|
| `AddOpenApi()` | the OpenAPI document generator | used only in Development, see below |
| `AddTicketsLocalization()` | localization and `ProblemDetails` with translated titles | see [LocalizationSetup.cs](#localizationlocalizationsetupcs) |
| `AddSingleton(TimeProvider.System)` | the clock | singleton; the create endpoint asks for `TimeProvider` instead of calling `DateTimeOffset.UtcNow`, so a test can swap in a fake clock |
| `AddSingleton<TicketStoreTestHooks>()` | the test hook holder | singleton, so a test sets it once and every request sees it |
| `AddTicketsDatabase()` | the connection string and `TicketsDbContext` | see [TicketsDatabase.cs](#dataticketsdatabasecs) |
| `AddScoped<ITicketStore, EfTicketStore>()` | the ticket store | scoped (one per request), because it uses the scoped `DbContext` |
| `AddTicketsAuth()` | JWT bearer authentication, the fallback and `Agent` authorization policies, and `ITicketAccess` | see [AuthSetup.cs](#authauthsetupcs) (T-006) |

A **lifetime** says how often DI creates a new instance: singleton = once per app, scoped = once per request, transient = every time it's asked for.

### Build and startup checks (lines 17 to 23)

```csharp
WebApplication app = builder.Build();
app.ValidateAuthSettings();
await app.InitializeTicketsDatabaseAsync();
```

`Build()` locks the service list and creates the app. Then two fail-fast checks run once, before the app listens:

- `ValidateAuthSettings` (T-006 criterion 8): outside Development, throws if the identity provider's authority or audience isn't configured, so the API never starts with authentication that can't work. Details in [AuthSetup.cs](#authauthsetupcs).
- `InitializeTicketsDatabaseAsync` fails fast if the connection string is missing, applies migrations in Development, and refuses to start in any other environment if migrations are pending. Details in [TicketsDatabase.cs](#dataticketsdatabasecs).

### Middleware pipeline (lines 25 to 40)

Middleware runs in the order it is added, so order matters here.

1. `app.UseTicketsLocalization()` comes **first**, so that everything after it, including the error handlers' `ProblemDetails`, is produced in the request's language.
2. `app.UseExceptionHandler(...)` catches any unhandled exception thrown later in the pipeline and writes a `ProblemDetails` response. No exception message or stack trace is sent to the client.
3. `app.UseStatusCodePages()` gives a body to error responses that have none (for example the empty 404 from `TypedResults.NotFound()`, or the empty 401 and 403 from authentication and authorization). Because `AddProblemDetails` is registered, that body is `ProblemDetails`.
4. `app.UseTicketsAuth()` adds authentication, then authorization (T-006). It comes **after** the error handlers, so a 401 or 403 passes back out through them and becomes a localized `ProblemDetails` with `Content-Language` (criteria 26 and 27). Details in [AuthSetup.cs](#authauthsetupcs).

**The exception handler's status selector** is the non-obvious part:

```csharp
StatusCodeSelector = ex => ex is BadHttpRequestException bad
    ? bad.StatusCode
    : StatusCodes.Status500InternalServerError,
```

By default the exception handler answers every exception with 500. But some exceptions are the client's fault, not the server's: a body over the 32 KB cap throws a `BadHttpRequestException` with status 413 while the body is read. The selector keeps that exception's own status (413, or a 400 for a bad body), and only real server failures become 500.

### Endpoints and run (lines 42 to 50)

- `app.MapOpenApi().AllowAnonymous()` only in Development: serves the OpenAPI JSON document describing the API, without a token. Not mapped in other environments, so there it gets 401 like any unknown path (T-006 criterion 5).
- `app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous()` is a lambda endpoint that always returns `200 {"status":"ok"}`, with or without a token. It doesn't touch the database. Useful for a load balancer or a quick "is it running" check.
- `AllowAnonymous()` is the opt-out from the fallback policy. These two are the only endpoints that use it (criterion 5).
- `app.MapTicketsEndpoints()` adds the `/tickets` routes from [TicketsEndpoints.cs](#featuresticketsticketsendpointscs).
- `app.Run()` starts Kestrel and blocks until the app shuts down.

### `public partial class Program { }`

With top-level statements the compiler generates a `Program` class that is not public. The integration tests use `WebApplicationFactory<Program>`, which needs to see it. This empty `partial` declaration merges with the generated class and makes it public. It adds no code.

---

## Auth/AuthSetup.cs

Source: [AuthSetup.cs](../../src/TicketsApi/Auth/AuthSetup.cs)

### `internal static class AuthSetup`

Authentication and authorization for the whole API (T-006). The API doesn't issue tokens; it accepts **JWT bearer tokens** from an OpenID Connect identity provider (Auth0, Okta or Keycloak are candidates; none is chosen). In Development the tokens come from `dotnet user-jwts` (criterion 7).

Two words that are easy to mix up:

- **Authentication** answers "who are you?": it reads the `Authorization: Bearer <token>` header, checks the token, and builds a `ClaimsPrincipal` (the user and their claims). Failure means **401**.
- **Authorization** answers "may you do this?": it checks policies against that user. Failure means **403** (or 401 if nobody is signed in).

**Constants**

| Constant | Value | Used for |
|---|---|---|
| `AgentPolicy` | `"Agent"` | the policy name on PATCH `/tickets/{id}/status` |
| `AuthorityKey` | `"Authentication:Schemes:Bearer:Authority"` | the provider's URL (its discovery document lives under it) |
| `AudienceKey` | `"Authentication:Schemes:Bearer:Audience"` | the audience the token must be issued for |
| `RoleClaimKey` | `"Authentication:RoleClaim"` | the name of the claim that holds roles |
| `DefaultRoleClaim` | `"role"` | used when `RoleClaimKey` isn't set; what `dotnet user-jwts` emits |
| `AsymmetricAlgorithms` (private) | RS256/384/512, PS256/384/512, ES256/384/512 | the only signing algorithms accepted outside Development |

All settings come from configuration, never from code (criteria 6 and 10). Outside Development they are set by environment variables, for example `Authentication__Schemes__Bearer__Authority`.

### `RoleClaim(IConfiguration configuration)`

```csharp
public static string RoleClaim(IConfiguration configuration)
```

Returns the configured `Authentication:RoleClaim` if it is non-empty, otherwise `"role"`. The name is used exactly as configured, so a namespaced name such as Auth0's `https://<namespace>/roles` works (criterion 10). Called by [TicketAccess.IsAgent](#featuresticketsticketaccesscs).

### `AddTicketsAuth(this IServiceCollection services)`

Registers everything; called from `Program.cs`. Returns `services`.

1. **Authentication.** `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer()` registers the JWT bearer scheme ("Bearer") as the default. `AddJwtBearer()` with no arguments also **binds configuration** from `Authentication:Schemes:Bearer` automatically: `Authority`, `ValidIssuer(s)`, `ValidAudience(s)`, `RequireHttpsMetadata`, and the signing keys `dotnet user-jwts` writes, among others. The comment notes it does **not** read `Audience`, which is why the next step does.
2. **Options configured from configuration.** `AddOptions<JwtBearerOptions>("Bearer").Configure<IConfiguration, IHostEnvironment>(...)` runs when the options are first used, not while `Program.cs` builds services, so environment variables and test hosts' settings always apply. It runs after the automatic binding above, so it has the last word. Step by step:
   - sets `Authority` from `AuthorityKey` and `Audience` from `AudienceKey`, if they are non-empty;
   - `MapInboundClaims = false`: keeps the token's own claim names. Without it, the handler renames some JWT claims to long .NET claim types (for example `sub` to a `nameidentifier` URI). With it, `sub` stays `sub` and the role claim keeps its configured name (criteria 4 and 10);
   - `NameClaimType = "sub"`, so `User.Identity.Name` is the subject;
   - sets `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, `ValidateIssuerSigningKey` and `RequireSignedTokens` to `true` (criterion 9). They are written out so no configuration binding or later package version can turn them off; the Production "wrong issuer" and "wrong audience" tests pin the behaviour. Clock skew stays at the default 5 minutes;
   - **outside Development only** (criteria 6 and 9, from the security audit):
     - `RequireHttpsMetadata = true`, so the provider's metadata is only fetched over HTTPS, even if configuration says otherwise;
     - `ValidAlgorithms = AsymmetricAlgorithms`, so a symmetric (HS*) token is rejected. This is what stops a `dotnet user-jwts` key put into Production configuration, because that binding only produces symmetric keys, and a symmetric key would let anyone holding it mint tokens. EdDSA isn't in the list;
     - `IssuerSigningKey` and `IssuerSigningKeys` set to `null`, so signing keys come only from the provider's discovery document. Today this is a backstop behind the algorithm list, in case a later version binds asymmetric keys from configuration.

     Development keeps the `dotnet user-jwts` keys;
   - `IncludeErrorDetails = false`: the `WWW-Authenticate` header of a 401 never has an `error_description` explaining why the token was rejected (criteria 26 and 28);
   - `Events.OnTokenValidated`: after the token passes validation, if it has no `sub` claim or an empty one, `context.Fail(...)` rejects it, so the request gets 401 (criterion 4).
3. **Authorization.** `AddAuthorizationBuilder()`:
   - `SetFallbackPolicy(... RequireAuthenticatedUser() ...)`: every endpoint that doesn't set its own authorization requires a signed-in user, including requests that match no route, so an anonymous caller can't learn which routes exist (criterion 1). Endpoints opt out with `AllowAnonymous()`;
   - `AddPolicy("Agent", ...)`: a signed-in user plus an `AgentRequirement`, decided by [AgentAuthorizationHandler](#authagentauthorizationhandlercs) (criterion 20).
4. Registers `AgentAuthorizationHandler` as an `IAuthorizationHandler` and `TicketAccess` as `ITicketAccess`, both singletons (neither holds per-request state).

### `ValidateAuthSettings(this WebApplication app)`

```csharp
public static void ValidateAuthSettings(this WebApplication app)
```

Called from `Program.cs` right after `Build()` (criterion 8).

1. In Development, returns at once: `dotnet user-jwts` needs neither setting.
2. Otherwise, for `AuthorityKey` and then `AudienceKey`: if the value is missing or blank, throws `InvalidOperationException` naming the key and the matching environment variable (`:` replaced by `__`). The app fails to start.
3. Builds the `JwtBearerOptions` now, through `IOptionsMonitor<JwtBearerOptions>.Get("Bearer")`, and discards the result (`_ =`). That runs the configuration above at startup, so any other invalid setting fails here instead of on the first request; the comment's example is an `http://` authority, which `RequireHttpsMetadata` rejects (criterion 9).

Returns nothing.

### `UseTicketsAuth(this WebApplication app)`

Adds `UseAuthentication()` then `UseAuthorization()` to the pipeline and returns `app`. `Program.cs` calls it after the exception handler and status code pages, so a 401 or 403 becomes localized `ProblemDetails` with `Content-Language` (criteria 26 and 27).

---

## Auth/AgentAuthorizationHandler.cs

Source: [AgentAuthorizationHandler.cs](../../src/TicketsApi/Auth/AgentAuthorizationHandler.cs)

### `internal sealed class AgentRequirement : IAuthorizationRequirement`

An empty marker class: the `Agent` policy's requirement, "the caller must be an agent" (criterion 20). In ASP.NET Core authorization, a **requirement** says what must be true and a **handler** decides whether it is. The class has no body (`;` instead of `{ }`).

### `internal sealed class AgentAuthorizationHandler(ITicketAccess access) : AuthorizationHandler<AgentRequirement>`

The handler for `AgentRequirement`. It gets `ITicketAccess` through its primary constructor, so "who is an agent" has a single owner, and a test host that replaces `ITicketAccess` also changes who passes the policy (criterion 21).

### `HandleRequirementAsync(AuthorizationHandlerContext context, AgentRequirement requirement)`

If `access.IsAgent(context.User)` is true, calls `context.Succeed(requirement)`. Otherwise it does nothing, which leaves the requirement unmet, so authorization fails: 403 for a signed-in caller. Returns `Task.CompletedTask` (the check is synchronous).

---

## Auth/ClaimsPrincipalExtensions.cs

Source: [ClaimsPrincipalExtensions.cs](../../src/TicketsApi/Auth/ClaimsPrincipalExtensions.cs)

### `internal static class ClaimsPrincipalExtensions`

`SubjectClaim = "sub"`: the claim that identifies a user (criterion 4). Because `MapInboundClaims` is off, it keeps its JWT name.

### `Subject(this ClaimsPrincipal user)`

```csharp
public static string Subject(this ClaimsPrincipal user)
```

Returns the caller's `sub` value. If it is missing or empty, throws `InvalidOperationException`. That should never happen: a token without a `sub` is already rejected during validation (`OnTokenValidated` in `AuthSetup`), so reaching the throw means a bug, not bad client input; it would become a 500 through the exception handler. Used by `CreateTicketAsync` (to store the creator) and by `TicketAccess`.

---

## Features/Tickets/TicketsEndpoints.cs

Source: [TicketsEndpoints.cs](../../src/TicketsApi/Features/Tickets/TicketsEndpoints.cs)

### `public static class TicketsEndpoints`

All `/tickets` endpoints and their input validation. It is a static class of **Minimal API** handlers: plain methods whose parameters ASP.NET Core fills in from the route, query string, body and DI container. It's the "validate at the boundary" layer from `CLAUDE.md`: nothing reaches the store until the input has been checked.

Since T-006, handlers also take a `ClaimsPrincipal user` parameter. Minimal APIs fill it with `HttpContext.User`, the user that authentication built from the token. Every endpoint here is protected by the fallback policy, so `user` is always signed in when a handler runs.

**Constants**

| Constant | Value | Used for |
|---|---|---|
| `TitleMaxLength` | 200 | validation, and the EF Core model's max length |
| `DescriptionMaxLength` | 4000 | same |
| `MaxRequestBodyBytes` | 32 * 1024 (32 KB) | request size cap on POST and PATCH |
| `MaxStatusWriteAttempts` (private) | 3 | how many times a status change retries before giving up with 409 |

Why 32 KB? The comment explains: the largest valid body, with every character `\u`-escaped, is about 25 KB, so 32 KB never rejects a valid ticket, but stops a client from sending megabytes.

**Return types.** Handlers return types like `Results<Ok<TicketResponse>, NotFound>`. This is a union of `TypedResults`: the compiler checks you only return one of the listed results, and the OpenAPI generator learns every possible response from the signature.

### `MapTicketsEndpoints(this IEndpointRouteBuilder app)`

An extension method, called from `Program.cs` as `app.MapTicketsEndpoints()`.

1. Creates a **route group** with `app.MapGroup("/tickets")`, so each route below is relative to `/tickets`.
2. Maps four routes:

| Method and route | Handler | Notes |
|---|---|---|
| `POST /tickets` | `CreateTicketAsync` | 32 KB body cap |
| `GET /tickets/{id:guid}` | `GetTicketAsync` | `:guid` constraint |
| `GET /tickets` | `ListTicketsAsync` | optional `?status=` |
| `PATCH /tickets/{id}/status` | `ChangeStatusAsync` | `RequireAuthorization("Agent")`, 32 KB body cap, no `:guid` constraint |

3. Returns `app`, so calls can be chained.

Every route also needs a signed-in user, through the fallback policy in [AuthSetup.cs](#authauthsetupcs); none of them opts out.

Non-obvious points:

- **`{id:guid}`** is a route constraint. A URL like `/tickets/abc` doesn't match the route at all. With a token, the client gets a 404 rather than a 400 (T-002 criterion 3); without one, the fallback policy answers 401 first (T-006 criterion 1).
- **PATCH has no `:guid` constraint** (T-006 criteria 20 and 22). The `Agent` policy only runs on a matched endpoint. With the constraint, a non-agent sending `/tickets/abc/status` would match nothing and get 404 instead of the required 403. So the route accepts any `{id}` as a string, the policy runs, and the handler parses the id itself.
- **`.WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodyBytes))`** attaches the body cap to that endpoint. The server enforces it while the body is read: reading past 32 KB throws a `BadHttpRequestException` with status 413, which the exception handler's status selector in `Program.cs` keeps as 413.

### `CreateTicketAsync` (POST /tickets)

```csharp
private static async Task<Results<Created<TicketResponse>, ValidationProblem>> CreateTicketAsync(
    CreateTicketRequest request, ClaimsPrincipal user, ITicketStore store,
    IStringLocalizer<Messages> localizer, TimeProvider timeProvider, CancellationToken cancellationToken)
```

Parameters: `request` is bound from the JSON body; `user` is the signed-in caller; `store`, `localizer` and `timeProvider` come from DI; `cancellationToken` is cancelled if the client disconnects. Reading and parsing the JSON body into `CreateTicketRequest` is done by the framework before this method runs. Any signed-in user may create a ticket, agent or not (T-006 criterion 14).

Steps:

1. Trims the title: `request.Title?.Trim()`. The description is not trimmed.
2. Calls `IsValid`. If it fails, returns **400** `ValidationProblem` with the field errors.
3. Builds a new `Ticket`:
   - `Id = Guid.CreateVersion7()`: a version 7 GUID, which starts with a timestamp, so ids created later are generally larger.
   - `Status = TicketStatus.Open`: every new ticket starts open.
   - `CreatedAt = timeProvider.GetUtcNow()`: always UTC, and from the injected clock so tests can control it.
   - `CreatedBy = user.Subject()`: the caller's `sub`, which later decides who may read the ticket (T-006 criterion 14). It is not in the response.
4. Saves it with `store.AddAsync`.
5. Returns **201 Created** with a `Location: /tickets/{id}` header and the ticket as the body.

### `IsValid` (private helper)

```csharp
private static bool IsValid([NotNullWhen(true)] string? title, string? description,
    IStringLocalizer<Messages> localizer, out Dictionary<string, string[]> errors)
```

Checks the T-001 rules and collects every error, so the client sees all problems at once.

- Title null or empty (this includes a whitespace-only title, which is empty after trimming): `title` -> `TitleRequired`.
- Title longer than 200: `title` -> `TitleTooLong`.
- Description longer than 4000: `description` -> `DescriptionTooLong`.

Lengths are `string.Length`, which counts UTF-16 code units, as the T-001 criteria require (an emoji can count as 2).

Returns `true` only if there are no errors. The error texts come from the localizer, so they are English or French.

**`[NotNullWhen(true)]`** tells the compiler's nullable analysis: "if this returns true, `title` is not null". That's why `CreateTicketAsync` can assign `Title = title` without a `!`. The final `title is not null &&` is redundant at runtime (a null title already added an error), but it lets the compiler prove the attribute's promise.

### `GetTicketAsync` (GET /tickets/{id})

```csharp
private static async Task<Results<Ok<TicketResponse>, NotFound>> GetTicketAsync(
    Guid id, ClaimsPrincipal user, ITicketStore store, ITicketAccess access, CancellationToken cancellationToken)
```

Loads the ticket with `store.GetAsync`. Returns **404** if there is none, or if `access.CanRead(user, ticket)` is false; otherwise **200** with the ticket. Someone else's ticket gets exactly the same 404 as an unknown id, so a caller can't find out that it exists (T-006 criterion 15). The 404 is empty here; `UseStatusCodePages` plus `AddProblemDetails` turn it into a `ProblemDetails` body on the way out.

### `ListTicketsAsync` (GET /tickets)

```csharp
private static async Task<Results<Ok<TicketListResponse>, ValidationProblem>> ListTicketsAsync(
    [FromQuery] string[]? status, ClaimsPrincipal user, ITicketStore store, ITicketAccess access,
    IStringLocalizer<Messages> localizer, CancellationToken cancellationToken)
```

1. `status` is bound as an **array** on purpose: if it were a single `string`, a repeated `?status=Open&status=Closed` couldn't be detected (T-002 criterion 15).
2. If no `status` is given, there is no status filter.
3. If more than one value is given, or the value isn't a status name, returns **400** with `status` -> `StatusMustBeOneOf`.
4. Otherwise calls `store.ListAsync(filter, access.ListCreatorFilter(user), ...)` and returns **200** with `TicketListResponse`. The creator filter is `null` for an agent (every ticket) and the caller's `sub` for anyone else, so a non-agent sees only the tickets they created (T-006 criterion 17). `[.. tickets.Select(ToResponse)]` is a collection expression with a spread: it builds the list of responses.

The creator filter and the order (newest first) are applied by the store, in the database, not in memory.

### `ChangeStatusAsync` (PATCH /tickets/{id}/status)

```csharp
private static async Task<Results<Ok<TicketResponse>, ValidationProblem, NotFound, ProblemHttpResult>>
    ChangeStatusAsync(string id, HttpRequest request, ClaimsPrincipal user, ITicketStore store,
        ITicketAccess access, IStringLocalizer<Messages> localizer, CancellationToken cancellationToken)
```

The most involved method. T-006 criterion 22 (which changes T-003 criterion 23) fixes the order of the checks: authentication (401) and the agent role (403) run in the middleware before this handler; then route (404 for a non-GUID id), content type (415), body (400), lookup and read authorization (404), transition (409).

**Why it reads the body itself.** It takes the raw `HttpRequest` instead of a bound request object, for two reasons given in the comments:

- The content type must be checked first. The framework's JSON binding accepts any `+json` type and would parse (and reject with 400) an `application/merge-patch+json` body before the handler runs, so the client would get 400 instead of 415.
- Every bad status (a number, the wrong type, a repeated key) must give a 400 that names `status`. Binding straight to `TicketStatus` would accept numbers and give generic errors.

Steps:

1. **Id.** `id` is a `string` because the route has no `:guid` constraint (see `MapTicketsEndpoints`). If `Guid.TryParse` fails, returns **404**, the same answer the constraint gives on GET (T-002 criterion 3).
2. **Content type.** If `IsPlainJson(request.ContentType)` is false, returns **415**.
3. **Parse.** `JsonDocument.ParseAsync(request.Body, ...)`. The size cap is enforced during this read (over 32 KB throws the 413 exception, which is not caught here and goes to the exception handler). Malformed JSON throws `JsonException`, which is caught and becomes **400** with `body` -> `BodyInvalid`. `using` disposes the `JsonDocument` (it rents pooled memory).
4. **Validate.** `TryReadRequestedStatus` either gives the requested `TicketStatus` or a set of errors -> **400**.
5. **Read, check, compare-and-swap loop:**

```csharp
for (int attempt = 1; ; attempt++)
{
    Ticket? ticket = await store.GetAsync(ticketId, cancellationToken);
    if (ticket is null || !access.CanRead(user, ticket)) return TypedResults.NotFound(); // 404
    if (ticket.Status == requested) return TypedResults.Ok(ToResponse(ticket));          // no-op, 200
    if (!TicketTransitions.IsAllowed(ticket.Status, requested) || attempt > MaxStatusWriteAttempts)
        return TransitionConflict(ticket.Status, requested);                            // 409
    Ticket? updated = await store.TryUpdateStatusAsync(ticketId, ticket.Status, requested, cancellationToken);
    if (updated is not null) return TypedResults.Ok(ToResponse(updated));               // 200
}
```

(Shortened; the real code uses braces.)

Returns:

- **404** if the id isn't a GUID, the ticket doesn't exist, or the caller may not read it. The read check uses the same `ITicketAccess.CanRead` as GET (T-006 criterion 21, from the T-003 security audit). Without it, the 409's `currentStatus` or the same-status 200 would reveal a ticket the caller can't read. Today only agents reach this handler and agents can read every ticket, so the check matters if `ITicketAccess` changes (a test host can replace it).
- **200** without writing if the ticket already has the requested status (T-003 criterion 7: same status is a no-op, not a transition).
- **409** if the change isn't allowed from the current status.
- **200** with the updated ticket if the write succeeds.

**Why a loop (concurrency).** Between reading the ticket and writing it, another request may change it. `TryUpdateStatusAsync` only writes if nothing changed in between; if it returns `null`, the loop reads the ticket again and re-evaluates against the *new* status. So every answer reflects a status the ticket really had (T-003 criterion 25). For example, if another request just moved the ticket to `Closed`, the next pass returns 409 with `currentStatus: Closed`.

**Why it always ends.** At most 3 writes are tried. On the 4th pass, `attempt > MaxStatusWriteAttempts` is true, so the method returns from the read (404, 200 no-op or 409) without writing again.

### `TryReadRequestedStatus` (private helper)

```csharp
private static bool TryReadRequestedStatus(JsonElement body, IStringLocalizer<Messages> localizer,
    out TicketStatus status, out Dictionary<string, string[]> errors)
```

Implements T-003 criteria 12 to 19.

1. If the JSON root is not an object (an array, a string, a number...), error `body` -> `BodyInvalid`.
2. Collects the values of every property named `status`, **ignoring case**, like the rest of the API's JSON binding. So `{"status": "Open", "Status": "Closed"}` counts as a repeated key. Other properties are ignored.
3. Uses a `switch` expression with **list patterns** to pick an error key:

| Values found | Result |
|---|---|
| none: `[]` | `StatusRequired` |
| one `null`: `[{ ValueKind: Null }]` | `StatusRequired` |
| one non-string (number, bool, object...) | `StatusMustBeOneOf` |
| one string that isn't a status name | `StatusMustBeOneOf` |
| one valid string: `[_]` | no error |
| two or more | `StatusRepeated` |

4. If there is an error key, adds `status` -> localized message and returns `false`. Otherwise returns `true`.

Non-obvious: `status` is set inside the `when` guard. The arm `[JsonElement value] when !TryParseStatus(value.GetString()!, out status)` calls `TryParseStatus`. If parsing succeeds, the guard is false, that arm is skipped, and the next arm `[_] => null` matches, with `status` already filled in. The `!` after `GetString()` is safe because the earlier arms have ruled out null and non-strings.

### `TryParseStatus` (private helper)

```csharp
private static bool TryParseStatus(string value, out TicketStatus status)
```

Loops over the defined `TicketStatus` values and returns the one whose name equals `value`, ignoring case (`"inprogress"` matches `InProgress`). Returns `false` if none matches.

Why not just `Enum.TryParse`? It also accepts numeric strings (even undefined ones like `"99"`), trims spaces, and accepts comma lists like `"Open,Closed"`. None of those should be valid input.

### `TransitionConflict` (private helper)

```csharp
private static ProblemHttpResult TransitionConflict(TicketStatus current, TicketStatus requested)
```

Returns a **409** `ProblemDetails` with two extra fields, `currentStatus` and `requestedStatus`, as status names. These explain the conflict with data instead of fixed English text, so the only text, the title, can be localized (T-004 criterion 10). The title itself is filled in by `CustomizeProblemDetails` in [LocalizationSetup.cs](#localizationlocalizationsetupcs).

### `IsPlainJson` (private helper)

```csharp
private static bool IsPlainJson(string? contentType)
```

Parses the `Content-Type` header with `MediaTypeHeaderValue.TryParse` and returns `true` only if the media type is exactly `application/json` (case-insensitive). Parameters such as `; charset=utf-8` don't change the media type, so they pass. A missing or unparseable header returns `false`, and so does `application/merge-patch+json`.

### `ToResponse` (private helper)

```csharp
private static TicketResponse ToResponse(Ticket ticket)
```

Copies a `Ticket` into a `TicketResponse`. This mapping is the only way data leaves the API, which is why the internal `Ticket` class can change without changing the JSON. It doesn't copy `CreatedBy`, so the creator never appears in a response (T-006 criterion 19).

---

## Features/Tickets/ITicketAccess.cs

Source: [ITicketAccess.cs](../../src/TicketsApi/Features/Tickets/ITicketAccess.cs)

### `internal interface ITicketAccess`

Who may do what with tickets (T-006). It is the one place that owns the access rules: GET, the list, PATCH and the `Agent` policy's handler all ask it, so a test host can replace it in one place (criterion 21). The implementation is [TicketAccess](#featuresticketsticketaccesscs).

| Method | Contract |
|---|---|
| `bool IsAgent(ClaimsPrincipal user)` | true when the caller's role claim (`Authentication:RoleClaim`) holds exactly `agent` (criteria 10 to 13) |
| `bool CanRead(ClaimsPrincipal user, Ticket ticket)` | agents read every ticket; anyone else only the tickets they created (criteria 15, 16 and 18) |
| `string? ListCreatorFilter(ClaimsPrincipal user)` | the creator to filter the list by: `null` for an agent (all tickets), otherwise the caller's `sub` (criterion 17) |

---

## Features/Tickets/TicketAccess.cs

Source: [TicketAccess.cs](../../src/TicketsApi/Features/Tickets/TicketAccess.cs)

### `internal sealed class TicketAccess(IConfiguration configuration) : ITicketAccess`

The rules of T-006 criteria 10 to 18. It takes `IConfiguration` through its primary constructor to read the role claim name, and is registered as a singleton in `AddTicketsAuth`. `AgentRole` (private constant) is `"agent"`.

### `IsAgent(ClaimsPrincipal user)`

1. Gets the role claim name from `AuthSetup.RoleClaim(configuration)` (the configured name, or `"role"`).
2. Returns `true` if any of the user's claims has that type and the value `"agent"`, compared with `StringComparison.Ordinal`: exact and case-sensitive, so `Agent` or `agents` is not an agent (criterion 11).

Non-obvious points from the comments: a role claim that holds a JSON array arrives as one claim per value, so `["support", "agent"]` works with the same `Any` check. Nested claims, such as Keycloak's `realm_access.roles`, are not read (criterion 13); the identity provider must put roles in a flat claim. The configuration is read on every call, not cached.

### `CanRead(ClaimsPrincipal user, Ticket ticket)`

Returns `true` if the caller is an agent, or if the ticket has a creator and it equals the caller's `sub` (`user.Subject()`). A ticket created before T-006 has `CreatedBy == null`, so only agents can read it (criterion 18).

### `ListCreatorFilter(ClaimsPrincipal user)`

Returns `null` for an agent (no creator filter: every ticket), otherwise `user.Subject()`.

---

## Features/Tickets/Ticket.cs

Source: [Ticket.cs](../../src/TicketsApi/Features/Tickets/Ticket.cs)

### `public enum TicketStatus`

The five statuses: `Open`, `InProgress`, `Resolved`, `InReview`, `Closed`.

`[JsonConverter(typeof(JsonStringEnumConverter<TicketStatus>))]` makes JSON use the names (`"InProgress"`) instead of numbers (`1`), both when writing responses and when binding. It's public because it appears in the public response records.

Order matters only in C#: the database stores the name, not the number (see `TicketsDbContext`), so reordering the enum can't corrupt stored data.

### `internal sealed class Ticket`

The domain entity, the shape stored in the database.

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | required |
| `Title` | `string` | required |
| `Description` | `string?` | optional |
| `Status` | `TicketStatus` | required |
| `CreatedAt` | `DateTimeOffset` | required, always UTC |
| `CreatedBy` | `string?` | optional: the creator's `sub` (T-006 criterion 14); `null` for tickets created before T-006 (criterion 18) |

- `required` means every object initializer must set the property, or the code doesn't compile.
- `init` means the property can be set only while the object is created. So a `Ticket` is **immutable**: a status change builds a new `Ticket` rather than changing one.
- `internal`: never exposed directly; responses use `TicketResponse`.
- `CreatedBy` decides who may read the ticket (see [TicketAccess](#featuresticketsticketaccesscs)). It is not `required`, because old rows have none, and `ToResponse` doesn't map it, so it never reaches a response (criterion 19).
- There is deliberately no `Version` property. The concurrency version lives only in the database (an EF Core shadow property), so it can never leak into a response (T-005 criteria 8 and 10).

It has no methods.

---

## Features/Tickets/TicketContracts.cs

Source: [TicketContracts.cs](../../src/TicketsApi/Features/Tickets/TicketContracts.cs)

The JSON shapes the API accepts and returns. They are `record`s: short, immutable, with value equality. Positional records like these generate a constructor and a property for each parameter. No methods.

### `CreateTicketRequest(string? Title, string? Description)`

The POST body. Both properties are nullable on purpose: a client can leave out `title`, and the endpoint must answer with a clear validation error instead of failing to bind.

### `TicketResponse(Guid Id, string Title, string? Description, TicketStatus Status, DateTimeOffset CreatedAt)`

One ticket in a response. Serialized with camelCase names (`id`, `title`, `description`, `status`, `createdAt`), the ASP.NET Core default for web JSON.

### `TicketListResponse(IReadOnlyList<TicketResponse> Items)`

The list response: `{"items": [...]}`. An object, not a bare array, so paging fields (planned for T-008) can be added later without breaking clients.

---

## Features/Tickets/TicketTransitions.cs

Source: [TicketTransitions.cs](../../src/TicketsApi/Features/Tickets/TicketTransitions.cs)

### `internal static class TicketTransitions`

The workflow rules from T-003 criterion 4, kept in one table:

| From | Allowed to |
|---|---|
| Open | InProgress, Closed |
| InProgress | Resolved |
| Resolved | Closed, InProgress, InReview |
| InReview | Closed, InProgress |
| Closed | (nothing) |

### `IsAllowed(TicketStatus from, TicketStatus to)`

Returns `true` if `to` is in the list for `from`. Changing to the same status is not a transition (`Open -> Open` returns `false`); the endpoint handles that case as a no-op *before* calling this. Every enum value has an entry, so the dictionary lookup can't throw for a defined status.

---

## Features/Tickets/ITicketStore.cs

Source: [ITicketStore.cs](../../src/TicketsApi/Features/Tickets/ITicketStore.cs)

### `internal interface ITicketStore`

What the endpoints need from storage. The endpoints depend on this interface, not on EF Core, so they hold the HTTP and business rules and the store holds the data access. The implementation is [EfTicketStore](#featuresticketsefticketstorecs).

| Method | Contract |
|---|---|
| `Task AddAsync(Ticket ticket, CancellationToken)` | saves a new ticket |
| `Task<Ticket?> GetAsync(Guid id, CancellationToken)` | the ticket, or `null` |
| `Task<IReadOnlyList<Ticket>> ListAsync(TicketStatus? status, string? createdBy, CancellationToken)` | all tickets (or only one status), newest first: `createdAt` descending, then `id` descending for ties (T-002 criteria 4 and 5). `createdBy` limits the list to the tickets that user created; `null` means every ticket (T-006 criterion 17) |
| `Task<Ticket?> TryUpdateStatusAsync(Guid id, TicketStatus expected, TicketStatus next, CancellationToken)` | atomic check-and-set: sets the status to `next` only if it is still `expected`. Returns the updated ticket, or `null` if the ticket is missing or its status changed (T-003 criterion 24) |

---

## Features/Tickets/EfTicketStore.cs

Source: [EfTicketStore.cs](../../src/TicketsApi/Features/Tickets/EfTicketStore.cs)

### `internal sealed class EfTicketStore(TicketsDbContext db, TicketStoreTestHooks testHooks) : ITicketStore`

The SQLite implementation through EF Core (T-005). It uses a **primary constructor**: the parameters `db` and `testHooks` are available in every method, with no field declarations. It is registered as scoped, so each request gets its own store and its own `DbContext`.

### `AddAsync(Ticket ticket, CancellationToken cancellationToken)`

1. `db.Tickets.Add(ticket)` starts tracking the new ticket.
2. Sets the shadow property `Version` to 0 explicitly. A shadow property exists in the database model but not on the C# class, so it's set through the change tracker entry: `entry.Property<int>("Version").CurrentValue = 0`.
3. `SaveChangesAsync` runs the `INSERT`.
4. In `finally`, `db.ChangeTracker.Clear()` forgets the tracked ticket, even if saving failed. Combined with untracked reads, this means the context never holds an old copy that could be served later in the same request.

Returns nothing. A database error propagates as an exception and becomes a 500 through the exception handler.

### `GetAsync(Guid id, CancellationToken cancellationToken)`

```csharp
db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
```

One `SELECT` by primary key. Returns the ticket or `null`. `AsNoTracking()` tells EF Core not to keep the result in the change tracker: faster for read-only queries, and nothing stale is left behind.

### `ListAsync(TicketStatus? status, string? createdBy, CancellationToken cancellationToken)`

1. Starts an untracked query.
2. `if (status is { } filter)` is a pattern that checks the nullable has a value and unwraps it into `filter`. If so, adds `WHERE Status = filter`.
3. If `createdBy` is not null, adds `WHERE CreatedBy = createdBy` (T-006 criterion 17). Rows with a null `CreatedBy` never match, so tickets from before T-006 only show up for agents, who pass `null` (criterion 18).
4. Orders by `CreatedAt` descending, then `Id` descending for ties, and runs the query with `ToListAsync`.

The filtering and sorting happen in the database (T-005 criterion 5), not in memory; the `CreatedBy` filter can use the index declared in `TicketsDbContext`. That works because `CreatedAt` is stored as an integer (see `TicketsDbContext`): SQLite can't sort a `DateTimeOffset`.

### `TryUpdateStatusAsync(Guid id, TicketStatus expected, TicketStatus next, CancellationToken cancellationToken)`

Optimistic concurrency, done by hand (T-005 criteria 8 and 9).

1. **Read** the ticket and its hidden `Version` in one query. `EF.Property<int>(t, "Version")` reads a shadow property in a query. The result is an anonymous type, which is why this line uses `var`: an anonymous type has no name you could write (the one allowed exception to the no-`var` rule from T-011).
2. If the ticket is missing, or its status is no longer `expected`, return `null`.
3. **Test hook.** If a test has set `testHooks.BeforeStatusWriteAsync`, await it. This lets a test pause the request exactly between the read and the write, while other requests change the ticket. In production it is `null` and is skipped.
4. **Conditional write.** `ExecuteUpdateAsync` sends one SQL statement, roughly:

```sql
UPDATE Tickets SET Status = @next, Version = @version + 1
WHERE Id = @id AND Version = @version
```

   It doesn't load entities or use the change tracker. If someone else wrote in between, the version no longer matches, so 0 rows change.
5. If 0 rows changed, return `null` (the endpoint will re-read and retry). Otherwise build and return a new `Ticket` with the new status and the other values from step 1, including `CreatedBy`. `CreatedBy` isn't `required`, so the compiler wouldn't complain if it were left out; it is copied so the returned ticket matches the stored row.

**Why a version and not the status as the check?** If another request changed `Open -> Closed -> Open` in between, a status check would still see `Open` and succeed. The version has gone up twice, so this write correctly fails. No transaction or lock is held between the read and the write; the `WHERE` makes the write itself atomic.

---

## Features/Tickets/TicketStoreTestHooks.cs

Source: [TicketStoreTestHooks.cs](../../src/TicketsApi/Features/Tickets/TicketStoreTestHooks.cs)

### `internal sealed class TicketStoreTestHooks`

A test-only seam (T-003 criteria 24 and 25). It has one property and no methods:

```csharp
public Func<Guid, CancellationToken, Task>? BeforeStatusWriteAsync { get; set; }
```

A function that `EfTicketStore.TryUpdateStatusAsync` awaits after reading a ticket and before writing it. Concurrency tests use it to hold one request at that point while another changes the ticket, which makes a rare race happen every time.

Why it's safe in production: it's `null` unless a test sets it, the class is `internal`, and no endpoint reads or writes it, so it can't be reached over HTTP. It's registered as a singleton so a test can set it once on the host and every per-request store sees it.

---

## Data/TicketsDbContext.cs

Source: [TicketsDbContext.cs](../../src/TicketsApi/Data/TicketsDbContext.cs)

### `internal sealed class TicketsDbContext(DbContextOptions<TicketsDbContext> options) : DbContext(options)`

The EF Core **DbContext**: a session with the database. It knows the model (tables, columns, conversions), tracks changes, and builds SQL from LINQ. It's scoped: one per request. The options (which provider, which connection string) come from `AddTicketsDatabase`.

### `VersionProperty` constant

`"Version"`, the name of the concurrency column. A **shadow property**: it's in the table and the EF Core model but not on the `Ticket` class, so it can never reach a response (T-005 criteria 8 and 10). It goes up by one on every status write.

### `Tickets` property

```csharp
public DbSet<Ticket> Tickets => Set<Ticket>();
```

The `Tickets` table as something you can query (`db.Tickets.Where(...)`) and add to.

### `OnModelCreating(ModelBuilder modelBuilder)`

EF Core calls this once to build the model. Each line records a storage decision made in T-005:

| Line | What it does | Why |
|---|---|---|
| `ToTable("Tickets")` | table name | explicit, not inferred |
| `HasKey(t => t.Id)` and `ValueGeneratedNever()` | `Id` is the primary key, set by the app | the endpoint creates a version 7 GUID; the database doesn't generate one |
| `HasMaxLength(200)` / `HasMaxLength(4000)` | max lengths for Title and Description | documentation only: SQLite doesn't enforce lengths; the endpoint is the validator (criterion 3) |
| `Status ... HasConversion<string>().HasMaxLength(20)` | stores the enum as its name, e.g. `"InProgress"` | reordering the enum can't change stored data (criterion 7) |
| `CreatedAt ... HasConversion(v => v.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero))` | stores the time as UTC ticks (100 ns units) in an INTEGER | SQLite can't `ORDER BY` a `DateTimeOffset`, but it can sort an integer; ticks round-trip exactly (criteria 4 and 5). Read back, the value is always UTC (offset zero) |
| `Property<int>(VersionProperty).IsConcurrencyToken()` | declares the shadow `Version` column | the version check in `EfTicketStore` |
| `Property(t => t.CreatedBy)` and `HasIndex(t => t.CreatedBy)` | the creator's `sub`, a nullable TEXT column, with an index (T-006 criteria 14 and 19) | nullable because tickets from before T-006 have no creator; indexed because a non-agent's list filters on it (criterion 17) |

The GUID key is stored as uppercase TEXT (the SQLite provider's default for `Guid`), which the comment notes sorts the same way as `Guid.CompareTo` (criterion 6). That keeps the "id descending for ties" order the same in SQL as in C#.

A note on `IsConcurrencyToken()`: EF Core uses a concurrency token automatically when it saves *tracked* changes with `SaveChanges`. The status update uses `ExecuteUpdateAsync`, which bypasses that, so `EfTicketStore` puts the version check in its own `WHERE`.

---

## Data/TicketsDatabase.cs

Source: [TicketsDatabase.cs](../../src/TicketsApi/Data/TicketsDatabase.cs)

### `internal static class TicketsDatabase`

Where the database is and what happens to its schema at startup (T-005 criteria 11, 12, 15 and 20). `ConnectionStringKey` is `"ConnectionStrings:Tickets"`, used in error messages. In environment variables it's written `ConnectionStrings__Tickets` (double underscore for `:`).

### `AddTicketsDatabase(this IServiceCollection services)`

Registers two things:

1. A singleton `TicketsConnectionString`, built by a factory lambda that calls `GetConnectionString`. The lambda runs **once per host, the first time someone asks for it**, not while `Program.cs` is building services. That matters: configuration added later (environment variables, or a test host overriding the connection string) is the one that's used.
2. `AddDbContext<TicketsDbContext>` with SQLite, reading the connection string from that singleton. `AddDbContext` registers the context as scoped.

Returns `services` for chaining.

### `InitializeTicketsDatabaseAsync(this WebApplication app)`

Called once from `Program.cs` before the app starts listening.

1. Resolves the connection string. If it's missing, `GetConnectionString` throws, and the app fails to start with a clear message (fail fast).
2. `CreateDatabaseFolder`: SQLite creates the file but not its folder, so `App_Data/` is created if needed.
3. Creates a DI scope (the `DbContext` is scoped, and there's no request yet) and gets the `Database` facade.
4. **Development:** `MigrateAsync()` applies any pending migrations, creating the database if needed, then returns.
5. **Every other environment:** asks for pending migrations. If there are any, throws `InvalidOperationException` listing them and telling you to run `dotnet ef database update`. The app refuses to start rather than run against an old schema or change a production schema by itself (criterion 20).

### `GetConnectionString(IConfiguration configuration, IHostEnvironment environment)`

Public so tests can call it.

1. Reads `ConnectionStrings:Tickets`. If it's missing or blank, throws `InvalidOperationException` explaining how to set it.
2. Parses it with `SqliteConnectionStringBuilder`.
3. If it's a plain file path and **relative**, makes it absolute against the app's **content root** (the project folder), not the current directory. So `Data Source=App_Data/tickets.dev.db` points at the same file however the API is started.
4. Returns the rebuilt connection string.

It has no side effects; creating the folder is a separate step.

### `CreateDatabaseFolder(string connectionString)`

`internal` so the tests can check it (criterion 11). If the connection string names a file, gets its folder and creates it with `Directory.CreateDirectory` (which does nothing if it already exists). In-memory and `file:` URI connection strings are left alone.

### `IsFile(SqliteConnectionStringBuilder builder)` (private)

Returns `true` for a plain file path. Returns `false` for in-memory databases (`Mode=Memory` or `Data Source=:memory:`), an empty data source, and `file:` URIs, which are used exactly as given.

### `internal sealed record TicketsConnectionString(string Value)`

A tiny wrapper around the resolved connection string, so it can be registered in DI as its own type. Registering a bare `string` would be ambiguous: any service asking for a `string` would get it.

---

## Data/DesignTimeTicketsDbContextFactory.cs

Source: [DesignTimeTicketsDbContextFactory.cs](../../src/TicketsApi/Data/DesignTimeTicketsDbContextFactory.cs)

### `internal sealed class DesignTimeTicketsDbContextFactory : IDesignTimeDbContextFactory<TicketsDbContext>`

Used only by the `dotnet ef` command-line tool, for example `dotnet ef migrations add`. When this class exists, the tool uses it to create a `TicketsDbContext` instead of starting the whole app.

### `CreateDbContext(string[] args)`

Returns a context configured with SQLite and the connection string `"Mode=ReadOnly"`, which names no database on purpose (T-005 criteria 11 and 12).

- Adding a migration only needs the model, not a database, so this works.
- For `dotnet ef database update`, you pass `--connection "<connection string>"`, which replaces this value entirely.
- If you forget `--connection`, SQLite would otherwise open a throwaway temporary database and report success while changing nothing. Opening it read-only makes the update fail instead, so the mistake is visible.

---

## Data/Migrations

Source: [Data/Migrations](../../src/TicketsApi/Data/Migrations/)

Generated by `dotnet ef migrations add`; don't edit them by hand. `InitialCreate` creates the `Tickets` table (TEXT `Id` primary key, TEXT `Title`, `Description` and `Status`, INTEGER `CreatedAt` and `Version`). `AddTicketCreatedBy` (T-006) adds a nullable TEXT `CreatedBy` column and the index `IX_Tickets_CreatedBy`; existing rows get `NULL`, which means "agents only" (criterion 18). Its `Down` drops the index, then the column. `TicketsDbContextModelSnapshot` records the current model so the tool can work out what the next migration must change. The `.editorconfig` marks this folder as generated code, so the T-011 style rules skip it.

---

## Localization/LocalizationSetup.cs

Source: [LocalizationSetup.cs](../../src/TicketsApi/Localization/LocalizationSetup.cs)

### `public static class LocalizationSetup`

T-004: error messages in English (the default) or French, chosen **only** from the `Accept-Language` header.

Two culture settings, deliberately different:

- `MessageLanguages` (`en`, `fr`) controls **`CurrentUICulture`**: which language `IStringLocalizer` returns.
- `FormattingCultures` (only `en`) controls **`CurrentCulture`**: number and date formatting and string comparison. It always stays English, so a client-controlled header can't change how the server parses or compares anything (T-004 criteria 13 and 14).

### `AddTicketsLocalization(this IServiceCollection services)`

1. `AddLocalization()` registers `IStringLocalizer<T>`, which reads `.resx` resource files.
2. Configures `RequestLocalizationOptions`:
   - default `en`;
   - supported cultures as described above;
   - `FallBackToParentUICultures = true`, so `fr-CA` or `fr-FR` fall back to `fr` (criterion 2). These are the framework defaults, set explicitly so the intent is visible;
   - `RequestCultureProviders` contains only the `Accept-Language` header provider. The framework's default list also allows a `?culture=` query string and a cookie; those are removed (criterion 6, T-002 criterion 8).
3. `AddProblemDetails` with a `CustomizeProblemDetails` callback that runs for **every** `ProblemDetails` the app writes: from validation, `TypedResults.Problem`, status code pages and the exception handler (criteria 9 and 10). It:
   - takes the status from the `ProblemDetails`, or else from the response;
   - picks a resource key: 400 is `Title400Validation` for a validation problem and `Title400` otherwise, then `Title401` and `Title403` (T-006 criterion 27), `Title404`, `Title405`, `Title409`, `Title413`, `Title415`, `Title500`; any other status gets no key;
   - if there is a key, sets `Title` to the localized text. The localizer is fetched from `context.HttpContext.RequestServices`, the current request's DI scope.

   It replaces any title an endpoint set (none does today) and leaves `Detail` alone; no error path sets it, and the exception handler never puts exception details in it.

Returns `services`.

### `UseTicketsLocalization(this IApplicationBuilder app)`

Must run first in the pipeline, before the exception handler, status code pages and authentication, so the errors they write (413, 500, the 404 for a non-GUID id, and since T-006 the 401 and 403) are localized too (criterion 10).

1. `UseRequestLocalization()` reads `Accept-Language` and sets the current cultures for the rest of the request.
2. A small inline middleware (the `app.Use((context, next) => ...)` lambda):
   - captures the chosen language name, e.g. `"fr"`;
   - registers `Response.OnStarting(...)`, which runs just before the response headers are sent, and adds `Content-Language: fr` and `Vary: Accept-Language` (criterion 7);
   - calls `next(context)` to continue the pipeline.

Why `OnStarting` instead of setting the headers right away? The exception handler clears the response headers before it writes its `ProblemDetails`. Headers added at "response starting" time are added after that, so they survive. `Vary: Accept-Language` tells caches that the response depends on that header.

Returns `app`.

---

## Localization/Messages.cs

Source: [Messages.cs](../../src/TicketsApi/Localization/Messages.cs)

### `public sealed class Messages`

A **marker type**: it has no members, and its private constructor stops anyone from creating one. It exists only so code can ask for `IStringLocalizer<Messages>`; the localizer uses the type's name to find `Messages.resx` (English) and `Messages.fr.resx` (French) in the same folder. The keys and wording were approved in T-004; T-006 added `Title401` ("Unauthorized" / "Authentification requise") and `Title403` ("Forbidden" / "Accès refusé").

Keys used by the code:

| Key | Used by |
|---|---|
| `TitleRequired`, `TitleTooLong`, `DescriptionTooLong` | `IsValid` (POST) |
| `StatusRequired`, `StatusMustBeOneOf`, `StatusRepeated`, `BodyInvalid` | `TryReadRequestedStatus`, `ChangeStatusAsync`, `ListTicketsAsync` |
| `Title400Validation`, `Title400`, `Title401`, `Title403`, `Title404`, `Title405`, `Title409`, `Title413`, `Title415`, `Title500` | `CustomizeProblemDetails` |

---

## TicketsApi.csproj

Source: [TicketsApi.csproj](../../src/TicketsApi/TicketsApi.csproj)

The project file. Package versions are not here; they live in `Directory.Packages.props`. T-006 added:

- `Microsoft.AspNetCore.Authentication.JwtBearer`: the JWT bearer authentication handler used by `AddJwtBearer()`.
- `<UserSecretsId>`: identifies this project's **user-secrets** store, a file in the developer's user profile, outside the repo. `dotnet user-jwts` keeps its Development signing key there, so the key is never committed (criterion 7). User-secrets are loaded only in Development.

---

## appsettings.json and appsettings.Development.json

Sources: [appsettings.json](../../src/TicketsApi/appsettings.json), [appsettings.Development.json](../../src/TicketsApi/appsettings.Development.json)

Not code, but they decide where the data goes and which tokens are accepted.

- `appsettings.json` (all environments): log levels and `AllowedHosts`. It has **no** connection string and no authentication settings, so outside Development you must supply both (for example the environment variables `ConnectionStrings__Tickets`, `Authentication__Schemes__Bearer__Authority` and `Authentication__Schemes__Bearer__Audience`), or startup fails.
- `appsettings.Development.json` (Development only, merged on top):
  - `ConnectionStrings:Tickets` = `Data Source=App_Data/tickets.dev.db`, resolved against the content root by `GetConnectionString`.
  - `Authentication:Schemes:Bearer`: `ValidIssuer` = `dotnet-user-jwts` and `ValidAudiences` = `["tickets-api"]`, the values `dotnet user-jwts create` writes. They are not secret and are committed (T-006 criterion 7). They use the `ValidIssuer` / `ValidAudiences` names because those are what `AddJwtBearer()`'s automatic configuration binding reads (see [AuthSetup.cs](#authauthsetupcs)).
