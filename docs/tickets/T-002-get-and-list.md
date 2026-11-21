# T-002 Get and list tickets

## Acceptance criteria
- GET `/tickets/{id}` returns `200` with the ticket, or `404` ProblemDetails if it doesn't exist.
- GET `/tickets` returns a list sorted newest first.
- GET `/tickets?status=Open` filters by status. An invalid status returns `400`.
