---
name: finish-ticket
description: Finishes the current ticket by verifying it, merging the feature branch into main and deleting the branch. Run as /finish-ticket.
disable-model-invocation: true
---

Finish the ticket on the current branch.

1. Run `git branch --show-current`. If it is `main` or doesn't start with `feature/`, stop. There is nothing to finish.
2. Run `dotnet build` and `dotnet test` and show the output. **If either fails, stop.** Never merge a red build.
3. Run `git status`. If there are uncommitted changes, show them and ask for a commit message, then `git add -A` and `git commit`.
4. Ask the `code-reviewer` subagent to review `git diff main...HEAD`. If it reports any **Must fix** items, stop and list them.
5. Merge into main, keeping the branch history visible:
   - `git checkout main`
   - `git pull`
   - `git merge --no-ff <branch> -m "Merge <branch>"`
   - If there are merge conflicts, stop and show them. Do not resolve them without approval.
6. Run `dotnet test` again on main. If it fails, stop and report it (the merge is not pushed yet).
7. Delete the branch with `git branch -d <branch>` (lowercase -d only refuses to delete unmerged work).
   If the branch was pushed, also delete the remote one: `git push origin --delete <branch>`.
8. Push main with `git push`. This asks for approval.
9. Summarize: the ticket, the acceptance criteria met, the commits merged, and the test results.
