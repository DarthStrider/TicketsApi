# T-013 Enforce HTTPS

> Not to be implemented now. Created during T-006 from the security audit (finding L3, Low), as T-006 criterion 29 requires. It is also listed in T-006 Out of scope. The criteria below are drafts and are **not yet approved** by the product owner.

**As a** support agent, **I want** the API to accept requests only over HTTPS **so that** my bearer token never travels in cleartext, even if the API is reached over plain HTTP by mistake.

## Background
Since T-006 every request carries a bearer token. `Program.cs` has no `UseHttpsRedirection`, no HSTS and no forwarded headers, so if the API is ever served over HTTP, tokens travel in cleartext. The security auditor recommends:
- For an API, reject non-HTTPS requests instead of redirecting them. A redirect still sends the token once in cleartext, and most API clients follow it without noticing.
- HSTS outside Development.
- Forwarded headers with known proxies, when TLS terminates at a proxy in front of the API.

## Draft acceptance criteria (not yet approved)

### Rejecting plain HTTP
1. Given a request over plain HTTP to any path (except those decided in open question 4), with or without a token, when it is sent, then it is rejected with the status decided in open question 1, as ProblemDetails with the checks of T-002 criterion 17. The request is never redirected (no `301`, `302`, `307` or `308`).
2. The HTTPS check runs before authentication. Given a plain HTTP request with no token, then the response is the HTTPS rejection, not `401`. **This would change T-006 criteria 1 and 22.** (See open question 5.)
3. The rejection never contains the request's token or headers.
4. The `title` of the rejection is localized as in T-004, with `Content-Language` and `Vary: Accept-Language`. The key and the English and French wording are to be approved by the product owner (open question 6).

### HSTS
5. Given any environment other than Development, when a request is served over HTTPS, then the response has a `Strict-Transport-Security` header with the values decided in open question 2.
6. Given Development, then no `Strict-Transport-Security` header is sent.

### Behind a proxy
7. Given TLS terminates at a proxy, when the proxy forwards a request with `X-Forwarded-Proto: https` from an address in the configured known proxies or networks, then the request is treated as HTTPS.
8. Given a request with `X-Forwarded-Proto: https` from an address that is **not** a known proxy, then the header is ignored, and a plain HTTP request is still rejected (criterion 1).
9. The known proxies or networks come from configuration, never from C# code. What happens when none is configured is open question 3.

### Development and tests
10. Development behaviour is as decided in open question 7.
11. Criteria 1, 2 and 5 to 8 each have an integration test. How the test host chooses HTTP or HTTPS is decided in open question 8.
12. Every existing test passes, with no assertion changed and no test deleted or skipped. Only the test host setup may change.

### Security
13. Run the `security-auditor` subagent. Finding L3 from the T-006 audit is closed.

## Open questions

1. Status code for a plain HTTP request.
   - (a) `400` ProblemDetails. Recommended: it is what Microsoft's guidance for web APIs suggests (reject rather than redirect), and clients understand it.
   - (b) `403` ProblemDetails. Can be confused with the T-006 role `403`.
   - (c) `421 Misdirected Request`. Precise but rarely handled by clients.
   - (d) Don't listen on HTTP at all (HTTPS-only Kestrel endpoints), so there is no response. Doesn't work when TLS terminates at a proxy, so it can only be an addition to (a), (b) or (c).

2. HSTS values outside Development.
   - (a) The framework default (`max-age` 30 days), no `includeSubDomains`, no `preload`. Recommended: a safe start; the API does not own the rest of its domain, and it can be raised later.
   - (b) `max-age` one year with `includeSubDomains`. Only if the owner of the domain agrees.
   - (c) Also `preload`. Not recommended: hard to undo.
   - Note: HSTS is honoured by browsers, not by most API clients, so criterion 1 is the main protection.

3. Forwarded headers.
   - Which headers are read?
     - (a) `X-Forwarded-Proto` and `X-Forwarded-For`. Recommended if the API will run behind a proxy: without `X-Forwarded-For`, every client has the proxy's address, so the T-007 rate limit per client address would put all clients in one bucket. **This would change how T-007 identifies the client.**
     - (b) Only `X-Forwarded-Proto`. Smallest change; the T-007 problem stays.
   - What if no known proxy is configured outside Development?
     - (a) Forwarded headers are ignored, and only direct HTTPS is accepted. Recommended: safe by default, and it works when there is no proxy.
     - (b) Startup fails, as for a missing authority in T-006 criterion 8. Forces a decision, but breaks deployments without a proxy.
   - Is the hosting (with or without a proxy) known yet? If not, recommended: implement (a) for both, so it works either way.

4. Paths exempt from the HTTPS rule.
   - (a) `/health` may be called over plain HTTP. Recommended: load balancer and container health probes often call the API directly over HTTP, and `/health` needs no token and returns no data.
   - (b) No exemption. Health probes must use HTTPS.

5. Order of checks.
   - (a) The HTTPS check comes first, before authentication (criterion 2), so a plain HTTP request gets the same answer with or without a token. Recommended: the token is already exposed, so the API should not even validate it, and the answer reveals nothing about the token.
   - (b) Authentication first, so `401` keeps priority as in T-006 criterion 22.
   - Sub-question: should the API also warn that a token sent over HTTP must be treated as leaked? Recommended: no change in the response; mention it in documentation only.

6. Message text for the rejection (only if the status is not already in T-004 or T-006). A key (for example `TitleHttpsRequired`) and English and French wording, for the product owner to approve, for example "HTTPS required" / "HTTPS obligatoire".

7. Development behaviour.
   - (a) Plain HTTP is allowed in Development, with no HSTS. Recommended: the project has no `launchSettings.json`, so `dotnet run --project src/TicketsApi` listens on `http://localhost:5000` only, and Development tokens come from `dotnet user-jwts` (T-006 criterion 7), which are local test tokens.
   - (b) Development also rejects plain HTTP, and developers run with the ASP.NET Core development certificate (`dotnet dev-certs https --trust`) and an HTTPS URL. Closer to production, but adds set-up for every developer.

8. Tests.
   - (a) The shared test factory uses an `https://localhost` base address by default, so existing tests are unchanged. The new tests create a client with an `http://` base address, and set `X-Forwarded-Proto` and the remote address for the proxy cases. The tests run in a non-Development environment so the rules are active. Recommended.
   - (b) The HTTPS rule is turned off in the test host. Not recommended: like the test authentication scheme in T-006 criterion 24, a rule that can be switched off by configuration could be switched off by mistake in production.

## Out of scope
- `UseHttpsRedirection`. Redirecting sends the token once in cleartext (see Background).
- Certificates, TLS versions and cipher suites. These belong to the hosting or the proxy.
- Choosing the hosting or the proxy.
- Changing the T-007 rate limit partition (per client address or per `sub`). Open question 3 only flags the interaction.
- Declaring the bearer security scheme in the OpenAPI document (a separate suggestion in T-006).
