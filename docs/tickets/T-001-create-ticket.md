# T-001 Create a ticket

**As a** support agent, **I want to** create a ticket **so that** customer issues are tracked.

## Acceptance criteria
- Given a valid title (1–200 chars) and description, when I POST `/tickets`, then I get `201 Created` with a `Location` header and the ticket with its `id`, `status = "Open"` and `createdAt`.
- Given an empty or too-long title (missing and whitespace-only titles count as empty), when I POST, then I get `400` with ProblemDetails naming the `title` field.
- Leading and trailing whitespace is trimmed from the title before it is validated and stored (`"  Printer  "` is stored as `"Printer"`).
- Description is optional. Given a description longer than 4000 chars, when I POST, then I get `400` with ProblemDetails naming the `description` field.
- Lengths are measured in UTF-16 code units (`string.Length`), so an emoji such as 😀 counts as 2.
- Unknown fields in the request body are ignored.
- Given a body that can't be read (empty, `null`, wrong JSON types, malformed JSON), when I POST, then I get `400` with ProblemDetails. A non-JSON content type gets `415` with ProblemDetails.

### Limits
- The request body is capped at 32 KB. A larger body gets `413` with ProblemDetails.
- A valid ticket never gets `413` because of how it is encoded: a maximum-length title and description are accepted both as raw UTF-8 and as fully `\u`-escaped JSON (about 25 KB).
