# T-007 Input hygiene and rate limiting

**As an** operator, **I want** ticket input to be free of invisible and control characters and clients to be rate limited, **so that** tickets display safely and one client can't overwhelm the API.

## Acceptance criteria
- A title made only of whitespace and invisible characters (zero-width space U+200B, word joiner U+2060, BOM U+FEFF) counts as empty.
- A title containing control characters (including tab and newline) or bidirectional override/isolate characters (U+202A–U+202E, U+2066–U+2069) is rejected with `400` naming `title`.
- A description containing control characters other than tab, carriage return and line feed is rejected with `400` naming `description`.
- Each client may POST `/tickets` at most 60 times per minute (configurable via `RateLimiting:Tickets`). Further requests get `429` with ProblemDetails.
- A client that disconnects mid-request (request aborted / `OperationCanceledException` from `RequestAborted`) is not reported or logged as a `500` server error. It is recorded at a lower level (for example status `499`, logged as information).
