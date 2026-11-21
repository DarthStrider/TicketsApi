# T-001 Create a ticket

**As a** support agent, **I want to** create a ticket **so that** customer issues are tracked.

## Acceptance criteria
- Given a valid title (1–200 chars) and description, when I POST `/tickets`, then I get `201 Created` with a `Location` header and the ticket with its `id`, `status = "Open"` and `createdAt`.
- Given an empty or too-long title, when I POST, then I get `400` with ProblemDetails naming the `title` field.
- Unknown fields in the request body are ignored.
