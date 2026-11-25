---
name: finish-ticket
description: After the user merged the ticket's pull request on GitHub, switches to main, pulls and deletes the merged feature branch. Run as /finish-ticket.
disable-model-invocation: true
---

Finish the ticket after the user merged its pull request on GitHub.

1. Run `git status`. If there are uncommitted changes, stop and ask what to do with them.
2. Note the current branch name, then `git checkout main` and `git pull`.
3. For the feature branch that was just merged:
   - Check it was merged: `gh pr list --state merged --head <branch>` must show the PR. If not, stop and say so.
   - Delete it locally with `git branch -d <branch>`. If git says it isn't fully merged (for example after a squash merge),
     stop and tell the user. **Never use `git branch -D`.**
   - If `origin/<branch>` still exists, ask before running `git push origin --delete <branch>`.
4. Run `git fetch --prune`, then `dotnet build` and `dotnet test` on main, and show the results.
5. Check that the lessons and the code guide arrived with the merge: `git log -1 --format=%s -- docs/learning/` on main
   should show `<ticket>: Learning notes and code guide` (from `/create-pr`). **Never commit on main**: it is protected.
   If they are missing, tell the user. If they want them, create a branch `docs/<ticket>-learning-notes`, ask the
   `teacher` subagent for the lessons and the code guide update, commit only `docs/learning/`, and ask before pushing
   and opening a pull request.
6. Summarize: main is at <commit>, the branch is deleted, the tests are green, and the lessons and code guide are in.
   Suggest `/start-ticket` for the next ticket.
