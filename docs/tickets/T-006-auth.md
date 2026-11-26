# T-006 Secure the API

**As a** support agent, **I want** the API to know who is calling and what they may do **so that** people see only the tickets they are allowed to see, and only agents can change a ticket's status.

## Acceptance criteria

### Authentication
1. Authentication is required on every path by default (a fallback policy), not only under `/tickets`. Given a request with no `Authorization` header to any path other than those in criterion 5, when it is sent, then the response is `401`. This includes paths that match no route (for example `/nothing` or `/tickets/abc`) and verbs that are not mapped, so an anonymous caller can't learn which routes exist. An endpoint added later is protected unless it explicitly opts out.
2. Given a bearer token that is malformed, uses another scheme (for example `Basic`), is expired, is not yet valid, is signed with another key, is unsigned or uses `alg: none`, or has the wrong issuer or the wrong audience, when it is sent to `/tickets`, then the response is `401`. Each case has its own test (see criterion 23).
3. A token is accepted only from the `Authorization: Bearer` header. Given a valid token sent only in the query string or in a cookie, when the request is sent to `/tickets`, then the response is `401`.
4. The user is identified by the token's `sub` claim. Given an otherwise valid token with no `sub`, or an empty `sub`, when it is sent to `/tickets`, then the response is `401`.
5. Only these endpoints opt out of authentication. Given no token, when I GET `/health`, then I get `200` as before. In Development, given no token, when I GET the OpenAPI document (`/openapi/v1.json`), then I get `200`. In other environments it is still not mapped, so given no token, it gets `401` like any other path (criterion 1).

### Configuration
6. The API works with any standard OpenID Connect provider (Auth0, Okta or Keycloak are candidates; none is chosen yet). The authority and the audience come from configuration under `Authentication:Schemes:Bearer`. Signing keys are found through the provider's discovery document and are asymmetric. No authority, audience, key or secret appears in C# code or in `appsettings.json` (the settings for every environment). Outside Development, they come from the environment (for example `Authentication__Schemes__Bearer__Authority`).
7. In Development, tokens come from `dotnet user-jwts`. The issuer and audiences that `dotnet user-jwts create` writes to `appsettings.Development.json` are not secret and are committed. The signing key is stored only in user-secrets and is never committed.
   - The Development audience was created with `dotnet user-jwts create --project src/TicketsApi --name dev-agent --role agent --audience tickets-api`. The project has no `launchSettings.json`, so the audience must be given explicitly with `--audience`.
8. Given no authority, or no audience, in configuration, when the API starts in any environment other than Development, then startup fails with an error that names the missing key. The API never starts with authentication turned off.
9. Issuer, audience, lifetime and signature are always validated. Clock skew is the framework default (5 minutes). Outside Development, the discovery metadata must be fetched over HTTPS.

### Roles
10. The name of the role claim comes from configuration (`Authentication:RoleClaim`). The default is `role`, which `dotnet user-jwts` emits. The name is used exactly as configured, including a namespaced name such as Auth0's `https://<namespace>/roles` or Okta's `groups`.
11. Given a role claim whose value is a single string or a JSON array of strings, when one of the values is exactly `agent` (case-sensitive), then the caller is an agent. Several roles are allowed. Given a value of `Agent` or `agents`, then the caller is not an agent.
12. Given the role claim name is set to a non-default name (for example `https://tickets.example/roles`), when a token carries `agent` under that name, then the caller is an agent. When a token carries `agent` only under `role`, then the caller is not an agent.
13. Nested claims are not read. For example, Keycloak's default `realm_access.roles` is ignored. The identity provider must be configured to put the roles in a flat claim (for Keycloak, a protocol mapper). Given a token with `agent` only inside a nested claim, then the caller is not an agent.

### Create
14. Given any authenticated user (agent or not), when they POST a valid ticket to `/tickets`, then they get `201` as in T-001, and the ticket stores their `sub` as its creator.

### Read
15. Given an authenticated user who is not an agent, when they GET `/tickets/{id}` for a ticket they created, then they get `200`. When they GET a ticket created by someone else, then they get `404` ProblemDetails, identical to the `404` for an unknown id.
16. Given an agent, when they GET `/tickets/{id}` for any existing ticket, then they get `200`.
17. Given a non-agent, when they GET `/tickets`, with or without `?status=`, then `items` holds only the tickets they created, still in the order of T-002 criteria 4 and 5. Given an agent, then `items` holds all tickets, as before.
18. Tickets created before T-006 have no creator. Given such a ticket, then only agents can read or list it.
19. The creator is stored in a new `CreatedBy` column, added by a migration. The creator never appears in any response body or header, so the response contract of T-001 to T-005 is unchanged.

### Change status
20. Given a non-agent, when they PATCH `/tickets/{id}/status`, then they get `403` ProblemDetails and the ticket is unchanged. This holds for any id (including an unknown one and their own ticket) and any body.
21. Before it evaluates the change, PATCH `/tickets/{id}/status` runs the same read authorization as GET `/tickets/{id}` (criteria 15 to 18). Given a ticket the caller may not read, when they PATCH its status, then they get `404` (not `403`, `409` or `200`). Otherwise the `409` (`currentStatus`) and the same-status `200` (the full ticket) would reveal the ticket's status and contents to a caller who can't read it. (From the T-003 security audit.)
    - The read authorization is one service (for example `ITicketAccess`), used by GET `/tickets/{id}`, GET `/tickets` and PATCH `/tickets/{id}/status`, and the test host can replace it.
    - Agents can read every ticket and non-agents are stopped by the `403` first (criterion 20), so no HTTP caller reaches this case today. It is shown by a test that replaces the read authorization service in the test host with one that denies the ticket. Then, when an agent PATCHes that ticket, they get `404`, and the ticket is unchanged.
22. **This changes T-003 criterion 23.** Checks on PATCH `/tickets/{id}/status` run in this order: authentication (`401`, including for a non-GUID id, see criterion 1), role (`403`), route (`404` for a non-GUID id), content type (`415`), body (`400`), lookup and read authorization (`404`), transition (`409`). On POST `/tickets`, the `401` comes before `413`, `415` and `400`.

### Tests
23. Criteria 2 to 4 are tested with real JWT bearer validation, using tokens signed with a key the test generates. No real identity provider is needed.
24. All other tests use a test authentication scheme. It lives only in the test project, is never registered by `src/`, and cannot be turned on by configuration or a request header in a non-test host.
25. Every existing test passes, with no assertion changed and no test deleted or skipped. Only the test host setup may change: by default, the shared test factory calls as an authenticated agent with a fixed `sub`.

### Errors
26. Every `401` and `403` is ProblemDetails with the checks of T-002 criterion 17 (content type `application/problem+json`, a `status` field equal to the HTTP status code, no exception details). It never contains token contents or token validation details.
27. The `title` of a `401` and a `403` is localized as in T-004 (criteria 8 to 11, 16 and 17), and the response has `Content-Language` and `Vary: Accept-Language` (T-004 criterion 7). This extends T-004 criterion 9 and its approved "Message text" table with these rows (wording approved by the product owner):

    | Key | Where | English | French |
    |---|---|---|---|
    | Title401 | title | Unauthorized | Authentification requise |
    | Title403 | title | Forbidden | Accès refusé |

28. Every `401` has a `WWW-Authenticate: Bearer` header (RFC 6750). It may include `error="invalid_token"`, but never `error_description`.

### Security
29. Run the `security-auditor` subagent. No High or Medium finding is left open. Every Low finding is fixed or recorded in a new ticket.

## Out of scope

Not to be implemented in T-006.

- Exposing the creator (`createdBy`) in ticket responses. This is a possible later ticket.
- Parsing nested role claims (for example Keycloak's `realm_access.roles`). The identity provider is configured to emit a flat claim instead (criterion 13).
- The API issuing its own tokens (a login endpoint).
- Choosing the identity provider (Auth0, Okta or Keycloak).
- Identifying the ticket owner by issuer and `sub` together, not `sub` alone (security audit finding L2). Recorded in T-012.
- Enforcing HTTPS: rejecting plain HTTP requests, HSTS and forwarded headers (security audit finding L3, and the HTTPS suggestion below). Recorded in T-013.
- Suggestions the product owner has not decided yet:
  - HTTPS redirection and HSTS (`Program.cs` has neither, and bearer tokens should not travel over plain HTTP).
  - Declaring the bearer security scheme in the OpenAPI document.
  - Rate limiting per `sub` instead of per client address (T-007).
  - Recording "who" changed a status as the `sub` (T-009).
  - Who may link tickets, using the same rules as criteria 15 to 21 (T-010).
