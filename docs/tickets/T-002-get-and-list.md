# T-002 Get and list tickets

**As a** support agent, **I want to** view a ticket and list tickets (optionally by status) **so that** I can find and follow up on customer issues.

## Acceptance criteria

### Get one ticket
1. Given a ticket was created, when I GET `/tickets/{id}` with its id, then I get `200` with the same body the POST `201` returned (`id`, `title`, `description`, `status`, `createdAt`).
2. Given no ticket has that id, when I GET `/tickets/{id}` with a well-formed GUID, then I get `404` with ProblemDetails.
3. The route is `/tickets/{id:guid}`. Given an id that is not a GUID (for example `/tickets/abc`), when I GET it, then I get `404` with ProblemDetails.

### List tickets
4. Given tickets exist, when I GET `/tickets`, then I get `200` with an object `{ "items": [ ... ] }` containing every ticket, sorted by `createdAt` newest first. The response has no `total` or paging fields.
5. Given two tickets with the same `createdAt`, when I GET `/tickets`, then the tie is broken by `id` descending.
6. Given no tickets exist, when I GET `/tickets`, then I get `200` with `{ "items": [] }`.
7. There is no paging in T-002: GET `/tickets` returns all tickets. Paging is covered by T-008. The test creates more tickets than any plausible page size (101) and checks that all of them come back.
8. Given an unknown query parameter (for example `/tickets?foo=1`), when I GET `/tickets`, then it is ignored and the response is the same as without it.

### Filter by status
9. Given tickets with different statuses, when I GET `/tickets?status=Open`, then I get `200` with only the tickets with that status in `items`, still in the order of criteria 4 and 5.
   - Note: while `Open` is the only status, the test checks that `?status=Open` returns the matching tickets in order. The test that other statuses are excluded is added in T-003.
10. Any value defined in `TicketStatus` is a valid filter, so statuses added later (T-003) are accepted without changing this ticket.
11. Matching is case-insensitive: `?status=open` returns the same result as `?status=Open`. The response always writes the status with its canonical name (for example `"Open"`), whatever case the request used.
12. Given a numeric status, when I GET `/tickets?status=0` or `/tickets?status=99`, then I get `400` with ProblemDetails naming `status` in `errors`. Numeric values are rejected whether or not they match a defined status.
13. Given a status name that is not defined in `TicketStatus` (for example `?status=Banana`), when I GET `/tickets`, then I get `400` with ProblemDetails naming `status` in `errors`.
14. Given an empty status (`/tickets?status=`), when I GET `/tickets`, then I get `400` with ProblemDetails naming `status` in `errors`.
15. Given a repeated status (`/tickets?status=Open&status=Open`), when I GET `/tickets`, then I get `400` with ProblemDetails naming `status` in `errors`.
16. Given a status with spaces around it (for example `?status=%20Open`), when I GET `/tickets`, then I get `400` with ProblemDetails naming `status` in `errors`. The value is not trimmed.

### Error format
17. Every `404` and `400` above uses the same ProblemDetails checks as T-001: content type `application/problem+json`, a `status` field equal to the HTTP status code, and no exception details. No fixed `title` or `detail` text is required, because T-004 localizes the messages.
