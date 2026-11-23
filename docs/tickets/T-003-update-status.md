# T-003 Change ticket status

**As a** support agent, **I want to** move a ticket through its workflow (Open, InProgress, Resolved, an optional InReview, Closed) **so that** everyone can see where each customer issue stands, and resolved work can be reviewed before it is closed.

## Acceptance criteria

### Statuses
1. `TicketStatus` has the values Open, InProgress, Resolved, InReview and Closed, in that order. Responses always write the canonical name exactly as shown (for example `"InProgress"`, `"InReview"`), never a number, space or underscore.
2. Given a valid ticket, when I POST `/tickets`, then it is still created with `status = "Open"` (T-001 is unchanged).

### Change status
3. Given a ticket with status Open, when I PATCH `/tickets/{id}/status` with `{ "status": "InProgress" }`, then I get `200` with the full ticket body (`id`, `title`, `description`, `status`, `createdAt`) and `status` is `"InProgress"`.
4. The allowed transitions are exactly these, and each one has a test. Given a ticket in the "from" status, when I PATCH the "to" status, then I get `200` with the new status:
   - Open → InProgress, Closed
   - InProgress → Resolved
   - Resolved → Closed, InProgress, InReview
   - InReview → Closed, InProgress
   - Closed → nothing (terminal)
5. Review is optional: Given a Resolved ticket, when I PATCH `{ "status": "Closed" }`, then I get `200`. Re-opening from review goes to InProgress: Given an InReview ticket, when I PATCH `{ "status": "InProgress" }`, then I get `200`.
6. Given a successful PATCH, when I GET `/tickets/{id}`, then it returns the new status, and `id`, `title`, `description` and `createdAt` are unchanged.
7. Given a ticket already in status X, when I PATCH `{ "status": "X" }`, then I get `200` with the unchanged ticket (a no-op). This holds for every status, including Closed.

### Invalid transitions
8. Given a ticket, when I PATCH a status that is not an allowed transition from its current status (criterion 4) and not its current status (criterion 7), then I get `409` with ProblemDetails. The tests cover at least: Open→Resolved, Open→InReview, InProgress→Open, InProgress→Closed, InProgress→InReview, Resolved→Open, InReview→Open, InReview→Resolved, and Closed→each other status (Open, InProgress, Resolved, InReview).
9. Closed is terminal. Given a Closed ticket, when I PATCH any status other than Closed, then I get `409`.
10. Given an invalid transition, when I get the `409`, then the ProblemDetails contains the extension fields `currentStatus` and `requestedStatus`, both as canonical names. There is no `allowedStatuses` field. No fixed `title` or `detail` text is required, because T-004 localizes the messages.
11. Given a PATCH was rejected (`409` or `400`), when I GET `/tickets/{id}`, then the ticket is unchanged.

### Request validation
12. Given a JSON object body with a `status` string, when I PATCH, then the status is matched case-insensitively against `TicketStatus` names, as in T-002 criterion 11 (`"inprogress"` and `"inReview"` are accepted), and the response uses the canonical name.
13. Given a body where `status` is missing or `null`, when I PATCH, then I get `400` with ProblemDetails naming `status` in `errors`.
14. Given a `status` name that is not defined (for example `"Banana"`, `"In Progress"` or `"in_progress"`), when I PATCH, then I get `400` with ProblemDetails naming `status` in `errors`.
15. Given a numeric `status`, as a JSON number (`1`) or as a string (`"1"`, `"99"`), when I PATCH, then I get `400` with ProblemDetails naming `status` in `errors`. Numbers are rejected whether or not they match a defined value.
16. Given an empty (`""`) or padded (`" InProgress"`, `"InProgress "`) `status`, when I PATCH, then I get `400` with ProblemDetails naming `status` in `errors`. The value is not trimmed.
17. Given a `status` of the wrong JSON type (boolean, array or object), when I PATCH, then I get `400` with ProblemDetails naming `status` in `errors`.
18. Given a body with `status` repeated (`{"status":"InProgress","status":"Closed"}`), when I PATCH, then I get `400` with ProblemDetails naming `status` in `errors`. The last value does not win.
19. Given a body with unknown fields next to a valid `status`, when I PATCH, then the unknown fields are ignored (same as T-001).
20. Given a body that can't be read (empty, `null`, a JSON array, malformed JSON), when I PATCH, then I get `400` with ProblemDetails. Given a content type other than `application/json` (including `application/merge-patch+json`), when I PATCH, then I get `415` with ProblemDetails.
21. The request body is capped at 32 KB, as in T-001. Given a larger body, when I PATCH, then I get `413` with ProblemDetails.

### Id and order of checks
22. Given no ticket has that id, when I PATCH `/tickets/{id}/status` with a well-formed GUID and a valid body, then I get `404` with ProblemDetails.
23. The route is `/tickets/{id:guid}/status`. Given an id that is not a GUID (for example `/tickets/abc/status`), when I PATCH it, then I get `404` with ProblemDetails. Checks run in this order: route (`404`), content type (`415`), body (`400`), lookup (`404`), transition (`409`). So given an unknown id and an invalid body, when I PATCH, then I get `400`. Other verbs on this route are not specified (framework default).

### Concurrency
24. A status change is checked against the ticket's current status and applied atomically (check-and-set). Given a Resolved ticket, when a PATCH to Closed and a PATCH to InProgress both read the ticket's current status before either one writes, then exactly one gets `200` and the other gets `409`, and the final status is the one that got `200`.
   - The test forces this interleaving deterministically through a test-only hook used by the store (an internal singleton, so the same test works with the EF Core store in T-005). The hook is `internal` (visible to the test project through `InternalsVisibleTo`), does nothing by default, and cannot be reached over HTTP.
   - The developer must show that this test fails when the atomic check is removed.
25. Given a status change whose write loses to a concurrent change of the same ticket, then it is re-evaluated against the ticket's new status, as in criteria 7 and 8: the same status gives `200` (no-op), an allowed transition is applied and gives `200`, and a transition that is not allowed gives `409`. It re-evaluates at most 3 times. If it still loses, it gets `409` whose `currentStatus` is the ticket's actual status at that moment (or `200` if that status equals the requested one). A `409` never reports a `currentStatus` the ticket did not have.
   - From Resolved, a paused PATCH to InProgress that loses to a PATCH to InReview is re-evaluated from InReview and gets `200`, and the final status is InProgress.
   - Two PATCHes to Closed on a Resolved ticket both get `200`.
   - A PATCH that loses 3 times gets `409` with the true `currentStatus`.
   - The tests force the order deterministically through the store's test-only hook.

### Filter by status (deferred from T-002 criterion 9)
26. Given tickets with different statuses (at least one each of Open, InProgress, Resolved, InReview and Closed, set through PATCH), when I GET `/tickets?status=<status>` for each of the five statuses, then I get only the tickets with that status, still in the order of T-002 criteria 4 and 5. This is the exclusion test deferred from T-002 criterion 9. Matching is case-insensitive for the new names too (for example `?status=inreview`).

### Error format
27. Every `400`, `404`, `409`, `413` and `415` above uses the same ProblemDetails checks as T-002 criterion 17: content type `application/problem+json`, a `status` field equal to the HTTP status code, and no exception details.
