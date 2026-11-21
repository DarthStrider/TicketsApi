---
name: start-ticket
description: Starts work on a ticket by creating a feature branch from an up-to-date main. Run as /start-ticket T-001.
argument-hint: <ticket-id, e.g. T-001>
disable-model-invocation: true
---

Start work on ticket **$ARGUMENTS**.

1. Find the ticket file in `docs/tickets/` whose name starts with `$ARGUMENTS`. If none matches, list the tickets and stop.
2. Run `git status`. If there are uncommitted changes, stop and ask what to do with them. Never stash or discard them yourself.
3. Switch to main and update it: `git checkout main`, then `git pull`.
4. Create the branch from the ticket file name: `git checkout -b feature/<ticket-file-name-without-.md>`
   (for example `feature/T-001-create-ticket`). If the branch already exists, ask before switching to it.
5. Read the ticket and list its acceptance criteria.
6. Explore the relevant code and propose a plan. **Wait for approval before writing any code.**
