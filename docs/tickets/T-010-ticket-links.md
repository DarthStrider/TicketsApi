# T-010 Link related tickets

> Not to be implemented now. Created during T-003, after the product owner decided that Closed tickets are never re-opened. The criteria below are drafts and are **not yet approved** by the product owner.

**As a** support agent, **I want to** link a ticket to related tickets **so that** follow-up work (a bug that came out of an issue, a refactor of it) stays traceable even though closed tickets can't be re-opened.

## Draft acceptance criteria (not yet approved)
- Given two existing tickets, when I link one to the other with a valid link type, then the link is stored and returned when I read the links of the source ticket.
- Given a Closed target ticket, when I link a new ticket to it, then the link is accepted. Closed tickets can be link targets; their status does not change.
- Given a target id that does not exist, when I create a link, then I get `404` with ProblemDetails. (See open question 7.)
- Error responses use the same ProblemDetails checks as T-002 criterion 17 and T-003 criterion 27.

## Open questions

1. Link types.
   - (a) A fixed set: `causedBy` ("bug caused by"), `refactorOf`, `relatesTo`, `duplicateOf`. Recommended: it covers the product owner's two examples plus the two most common generic links, and a fixed set is easy to validate (like `TicketStatus`).
   - (b) Only `causedBy` and `refactorOf`, the two examples given.
   - (c) Free-text link types. Not recommended: they can't be validated and drift over time.

2. Direction, and whether the inverse is shown on the other ticket.
   - (a) Links are directed (new ticket → original). Reading the links of either ticket shows the link, with the inverse shown on the target (for example "has bug" / "has refactor"). Recommended: the original ticket then shows its follow-up work, which is the point of the feature.
   - (b) Directed, shown only on the source ticket.
   - (c) Undirected (both tickets show the same "related" link). Loses the "caused by" meaning.

3. Links to or from Closed tickets.
   - (a) Links can be added to and from tickets in any status, including Closed. Recommended: the product owner's intent requires linking a new ticket to a closed one, and there is no reason to block the other direction.
   - (b) Closed tickets can be targets, but not sources.
   - (c) No links involving Closed tickets. Contradicts the product owner's intent.

4. Removing a link.
   - (a) A link can be removed (DELETE), for example to fix a mistake. Recommended.
   - (b) Links are permanent (an audit trail).
   - (c) Links can be removed only while neither ticket is Closed.

5. Self-links and duplicate links.
   - (a) A self-link gets `400`. An identical link (same source, target and type) is idempotent and returns the existing link with `200`. Recommended: mirrors the T-003 same-status no-op.
   - (b) A self-link gets `400`, and an identical link gets `409`.
   - (c) Both are allowed. Not recommended.
   - Sub-question: may two tickets have several links of different types (for example both `relatesTo` and `duplicateOf`)? Recommended: yes.

6. Endpoint shape.
   - (a) A sub-resource: POST `/tickets/{id}/links` with `{ "targetId": "...", "type": "causedBy" }`, GET `/tickets/{id}/links`, and DELETE `/tickets/{id}/links/{linkId}` if links can be removed. Recommended: it keeps the ticket body and T-002 list unchanged, the same way T-003 used `/tickets/{id}/status`.
   - (b) Links included in the ticket body returned by GET `/tickets/{id}`. Changes the existing response contract.
   - (c) A top-level `/links` resource.

7. Unknown ids.
   - (a) An unknown or non-GUID source id gets `404` (as in T-002 criterion 3). An unknown target id in the body gets `404` with ProblemDetails. Recommended: any unknown ticket is a `404`, as in T-002 and T-003.
   - (b) An unknown target id gets `400` naming `targetId` in `errors`, because it is invalid input in the body, while the `404` is kept for the source in the URL. Defensible, and clearer about which id is wrong.
   - (c) An unknown target id gets `422`.

8. Ordering relative to other tickets.
   - (a) Implement after T-005 (EF Core), so links are a table with foreign keys from the start, and after T-006 (auth), so the rules for who may create or remove links are decided with the rest of the authorization. Recommended.
   - (b) Implement before T-005 on the in-memory store, and migrate it in T-005. Adds work to T-005.
   - Sub-question for T-006: may any authenticated user link tickets, or only the `agent` role (like status changes)? And should linking run the same read authorization as GET `/tickets/{id}` on both tickets, so that a `404` or `409` doesn't reveal a ticket the caller can't read (like the T-006 PATCH rule)? Recommended: `agent` only, with read authorization on both tickets.
