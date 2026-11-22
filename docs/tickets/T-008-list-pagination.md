# T-008 Paginate the ticket list

> Not to be implemented now. Created during T-002 to hold paging. The criteria below are drafts and are **not yet approved** by the product owner.

**As a** support agent, **I want** the ticket list to be returned in pages **so that** the list stays fast and bounded as the number of tickets grows.

## Draft acceptance criteria (not yet approved)
- GET `/tickets` keeps the `{ "items": [ ... ] }` shape from T-002. Paging fields are added to the object without removing or renaming `items`, so existing clients keep working.
- Open question: the paging style (page/pageSize, or a cursor), the default and maximum page size, and whether a `total` field is returned.
- Open question: the status code and ProblemDetails for invalid paging parameters (for example a negative or too-large page size).
- Paging works together with the `status` filter and the newest-first ordering from T-002.
