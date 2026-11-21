---
name: create-pr
description: Verifies the current ticket branch, pushes it and opens a pull request for the user to review and merge on GitHub. Run as /create-pr.
disable-model-invocation: true
---

Open a pull request for the ticket on the current branch. **Never merge the PR yourself.** The user reviews and merges it on GitHub.

1. Run `git branch --show-current`. If it is `main` or doesn't start with `feature/`, stop. There is no ticket branch to open a PR for.
2. Run `gh auth status`. If GitHub CLI isn't logged in, stop and tell the user to run `gh auth login` in their terminal.
3. Run `dotnet build` and `dotnet test` and show the output. **If either fails, stop.**
4. Run `git status`. If there are uncommitted changes, show them and ask for a commit message, then `git add -A` and `git commit`.
5. Ask the `code-reviewer` subagent to review `git diff main...HEAD`. If it reports any **Must fix** items, stop and list them.
6. Bring the branch up to date: `git fetch origin`. If `origin/main` has commits the branch doesn't, run `git merge origin/main`.
   If there are conflicts, stop and show them. Otherwise run `dotnet test` again and stop if it fails.
7. Draft the pull request and **show it to the user for approval before creating it**:
   - **Title:** `T-00X: <ticket title>`
   - **Body:**
     - `## Summary`: what changed and why, in 2–4 sentences
     - `## Acceptance criteria`: a checklist with every criterion from the ticket file, each with the test(s) that cover it
     - `## Scope changes`: anything added to or removed from the ticket during the work, and new tickets created (e.g. T-007)
     - `## Tests`: the `dotnet test` totals (passed/failed/skipped) and the build warnings count
     - `## Review notes`: Should-fix and Nice-to-have items from the code review that were deferred
   Ask: "Create this PR?" Apply any edits the user asks for. **Do not continue without a yes.**
8. Push the branch: `git push -u origin <branch>` (asks for approval).
9. Write the body to a temporary file and create the PR:
   `gh pr create --base main --head <branch> --title "<title>" --body-file <file>`, then delete the temporary file.
10. Show the PR URL and stop. Tell the user: CI runs on the PR, and when it's green they review and merge it on GitHub.
    After merging, they run `/finish-ticket` to switch to main, pull and delete the branch.
