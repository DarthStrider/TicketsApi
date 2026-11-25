# T-012 Identify the ticket owner by issuer and subject

> Not to be implemented now. Created during T-006 from the security audit (finding L2, Low), as T-006 criterion 29 requires. The criteria below are drafts and are **not yet approved** by the product owner.

**As a** support customer, **I want** my tickets to be tied to my account at my identity provider, not only to my user id, **so that** a user with the same id at another identity provider can never read my tickets.

## Background
T-006 stores the token's `sub` claim as the ticket's creator (`CreatedBy`) and uses it alone to decide who may read a ticket (T-006 criteria 14 to 18). A `sub` is unique only within one issuer. Today exactly one issuer is trusted, so this is safe. If more than one issuer is ever trusted (a move to another identity provider, a multi-tenant Auth0 or Okta set-up, or `ValidIssuers` with several entries in configuration), user "X" at issuer B could read the tickets of user "X" at issuer A.

## Draft acceptance criteria (not yet approved)
Which set applies depends on open question 1.

### If option 1(a): store the issuer with the subject
1. Given an authenticated user whose token has issuer `A` and `sub` `X`, when they POST a valid ticket, then they get `201` as in T-001, and the ticket stores both `A` and `X` as its creator.
2. Given a ticket created by issuer `A`, `sub` `X`, when a non-agent with issuer `A`, `sub` `X` GETs it, then they get `200`.
3. Given a ticket created by issuer `A`, `sub` `X`, when a non-agent with issuer `B`, `sub` `X` GETs `/tickets/{id}`, then they get `404` ProblemDetails, identical to the `404` for an unknown id. When they GET `/tickets`, then the ticket is not in `items`. When they PATCH its status, the T-006 rules apply unchanged (criteria 20 and 21).
4. The read authorization service of T-006 criterion 21 is the only place that compares the creator, so GET `/tickets/{id}`, GET `/tickets` and PATCH `/tickets/{id}/status` all use the same issuer-and-subject rule.
5. Tickets created under T-006 (with a `sub` but no issuer) are handled as decided in open question 3.
6. Tickets created before T-006 (no creator) are handled as decided in open question 4.
7. The schema change is made by a migration. The issuer never appears in any response body or header, so the response contract of T-001 to T-006 is unchanged.
8. Criteria 1 to 3 are tested with two different issuers in the test host.

### If option 1(b): trust exactly one issuer
1. Given configuration that trusts more than one issuer (for example `ValidIssuers` with two entries), when the API starts in any environment, then startup fails with an error that names the configuration key. (Whether Development is included is open question 5.)
2. Given configuration that trusts exactly one issuer, when the API starts, then it starts as in T-006.
3. The single-issuer rule, and the reason for it (a `sub` is unique only within one issuer), is documented next to the authentication configuration. Where it is documented is to be decided.
4. Before a second issuer is ever trusted, option 1(a) must be implemented first. This ticket records that as a known limitation.

### Both options
- Every existing test passes, with no assertion changed and no test deleted or skipped.
- Run the `security-auditor` subagent. Finding L2 from the T-006 audit is closed.

## Open questions

1. Approach.
   - (a) Store the issuer together with `sub` as the creator, and compare both. Needs a migration and a decision about existing rows (questions 3 and 4). Recommended: no identity provider is chosen yet (T-006 Out of scope), so a later move to another provider is likely, and the cost of the backfill grows with the number of tickets.
   - (b) Trust exactly one issuer, enforce it at startup and document it. No schema change. Cheaper now, but a provider migration later needs option (a) anyway, and existing tickets would then have no issuer to compare.

2. How the issuer is stored (only for option 1(a)).
   - (a) A separate column (for example `CreatedByIssuer`) next to `CreatedBy`. Recommended: no delimiter to escape, and both parts can be queried and indexed.
   - (b) One composite value in `CreatedBy` (for example `issuer|sub`). No new column, but needs a delimiter that can't appear in either part, and every comparison must build the same string.
   - Sub-question: is the issuer compared exactly as it appears in the validated `iss` claim (case-sensitive, trailing slash kept, as Auth0 issues it)? Recommended: yes, exactly as validated, with no normalization.

3. Tickets created under T-006 (a `sub` but no issuer).
   - (a) Backfill them with the single issuer trusted when the migration is applied. Recommended: creators keep access to their own tickets, and today only one issuer is trusted, so the value is known. How the value is supplied to the migration (for example a migration parameter or a separate SQL step) is to be decided with the `database-engineer`.
   - (b) Leave the issuer empty, and treat an empty issuer as matching the configured issuer while only one issuer is trusted. No backfill, but the rule must change again when a second issuer is added.
   - (c) Treat them like tickets with no creator (agents only). Simplest, but their creators lose access to their own tickets.

4. Tickets created before T-006 (no creator).
   - (a) Unchanged: only agents can read or list them (T-006 criterion 18). Recommended: there is no creator to backfill.
   - (b) Something else (please specify).

5. Development (only for option 1(b)).
   - (a) The single-issuer check applies in every environment, including Development. Recommended: `dotnet user-jwts` uses one issuer (`dotnet-user-jwts`), so it costs nothing and keeps the environments alike.
   - (b) The check applies only outside Development.

6. Roles from more than one issuer (only relevant if several issuers are trusted).
   - When several issuers are trusted, is an `agent` role from any of them accepted? This is about who is an agent, not about ticket ownership. Recommended: keep it out of this ticket and record it as a separate ticket if a second issuer is ever planned.

## Out of scope
- Choosing the identity provider (as in T-006).
- Trusting a second issuer. This ticket only makes it safe, or explicitly blocks it.
- Exposing the creator (`createdBy`) or the issuer in ticket responses (as in T-006).
- Rules for roles from several issuers (open question 6).
