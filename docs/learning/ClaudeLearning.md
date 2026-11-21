# Claude Learning

What went wrong, what was decided and why, while building TicketsApi with AI agents. Each lesson ends with what to know and a line you can use in an interview. The **teacher** agent adds new lessons after each ticket.

## How to read this

- **What happened:** the real event in this repo.
- **Why:** the root cause.
- **Know this:** the concept to understand.
- **Interview line:** how to say it in one or two sentences.

## Quick reference

| Principle | One line |
|---|---|
| Build first, review second | The compiler and tests catch more than reading does. |
| Hooks for guarantees, agents for judgement | Anything that must always happen is a hook or a permission. |
| Every criterion has a test | And every test must be able to fail. |
| The ticket is the source of truth | Change the spec first, then the tests, then the code. |
| Least privilege | Broad freedom inside a sandbox you can roll back; approval for anything irreversible. |
| Agents propose, the human decides | Product decisions stay with the product owner. |

## Part 1: Setting up the project

### 1. Missing package for AddOpenApi

- **What happened:** the first build failed with CS1061: `IServiceCollection` has no `AddOpenApi`.
- **Why:** the agent used `AddOpenApi()` from memory of the .NET template but never referenced the `Microsoft.AspNetCore.OpenApi` package.
- **Know this:** CS1061 on an extension method almost always means a missing package or `using`. Check that every API maps to a reference.
- **Interview line:** "AI code often uses real APIs without the reference that provides them, so the build is my first review step."

### 2. Stale and deprecated packages

- **What happened:** package versions were written from memory. ASP.NET was 10.0.0 (latest patch 10.0.15), and the `xunit` v2 package is deprecated in favour of `xunit.v3`.
- **Why:** an agent's knowledge has a cutoff date. Versions need a live check.
- **Know this:** check each package on nuget.org for the latest version and any deprecation notice. Keep `Microsoft.AspNetCore.*` on the same major version as the target framework. We stayed on **.NET 10 (LTS)** even though .NET 11 had shipped, on purpose.
- **Interview line:** "I never trust a version number an agent wrote from memory, and LTS vs latest is a decision I make deliberately."

### 3. The upgrade broke the test runner

- **What happened:** after moving to xunit v3, `dotnet test` failed: VSTest is not supported by Microsoft.Testing.Platform on the .NET 10 SDK.
- **Why:** xunit v3 runs on **Microsoft.Testing.Platform (MTP)**. The .NET 10 SDK needs a `global.json` opt-in to use it.
- **Know this:** every upgrade has side effects. Read the migration guide and list every change: csproj, `global.json`, CI.
- **Interview line:** "One fix caused the next bug, which is why I re-run everything after each change."

```
global.json
{ "test": { "runner": "Microsoft.Testing.Platform" } }
```

### 4. Output the agent couldn't verify

- **What happened:** the setup agent had no .NET SDK, so it couldn't build or test its own work.
- **Know this:** "done" means nothing until it's built and tested. Give agents an environment where they can verify, and require them to show test output.
- **Interview line:** "The right behaviour was the agent saying it couldn't verify. The fix was making verification possible."

### 5. Permissions and least privilege

- **What happened:** folder access was refused until the exact folder was named. Deleting was off by default.
- **Know this:** grant the least access the task needs. Allow routine commands, require approval for risky ones (push, deletes, package installs), and deny dangerous ones (force push, `reset --hard`, merging PRs).
- **Interview line:** "I give agents broad autonomy inside a sandbox I can roll back, and hooks and permissions enforce the rest."

## Part 2: T-001 Create a ticket

### 6. The agent found a gap in the spec

- **What happened:** the ticket didn't say how to validate `description`. The agent asked instead of guessing.
- **Decision:** optional, max 4000 characters. A limit prevents unbounded input, and making it required would add a rule nobody asked for.
- **Know this:** when the spec has a gap, decide and **update the ticket**, so the spec and the tests stay in step.
- **Interview line:** "When an agent finds a gap, I fix the spec, not just the code."

### 7. Scope creep, three times

- **What happened:** I wanted more statuses than `Open` (they belong in T-003). Then the agent added rate limiting and "input hygiene" rules to T-001 without being asked.
- **Decision:** kept T-001 tight and moved the good ideas into a new ticket, **T-007**, not implemented yet.
- **Know this:** approving edits one by one is what caught it. Good ideas go into a ticket. They don't get silently added or thrown away.
- **Interview line:** "An agent widened the scope inside an otherwise good edit. I caught it at the prompt and turned it into its own ticket."

### 8. How to count characters

- **What happened:** the agent recommended counting *visible* characters for the 200 and 4000 limits.
- **Why I rejected it:** a single visible character can be any number of bytes (a family emoji is about 25 bytes, and stacked accents are unbounded), so a "valid" ticket could exceed the 32 KB body cap.
- **Decision:** count **UTF-16 code units** (`string.Length`). It's bounded, it's the .NET default, and it matches SQL Server `nvarchar`. An emoji counts as 2.
- **Interview line:** "The agent's recommended option was wrong, and I could explain exactly why: unbounded byte size."

### 9. A valid request must never be rejected because of its encoding

- **What happened:** an option suggested that an escaped-JSON valid ticket "may get 413".
- **Decision:** a valid ticket is always accepted, whether sent as raw UTF-8 or fully `\u`-escaped (about 25 KB max, under the 32 KB cap). This became an acceptance criterion with tests.
- **Know this:** don't accept a bug because an option offers it. State the rule, then test it.

### 10. The test server didn't behave like the real one

- **What happened:** the 413 test could never fail, because the in-memory `TestServer` ignores request size limits.
- **Fix:** run size-limit tests on the real **Kestrel** server, on a **random free port** (port 0) so tests can run in parallel and in CI.
- **Know this:** know what your test host can and can't simulate.
- **Interview line:** "A test was green for the wrong reason. I moved it to the real server and proved it could fail."

### 11. Prove a test can fail

- **What happened:** the agent temporarily removed the 32 KB cap, watched the test go red, then restored it.
- **Know this:** a test that has only ever been green proves nothing until you've seen it fail. Breaking the code on purpose to check a test is called mutation testing.

### 12. Development and Production behave differently

- **What happened:** the manual probe saw a bare 400, but the tests (running in Development) saw the developer exception page returning the **full exception**.
- **Fix:** `UseExceptionHandler` plus `UseStatusCodePages` with ProblemDetails, tested in both environments.
- **Know this:** `WebApplicationFactory` runs in Development by default. Test what Production clients actually see, and never leak stack traces.

### 13. Reviewers can be wrong too

- **What happened:** the reviewer flagged `/tickets/` vs `/tickets`. Testing showed `MapPost("")` and `MapPost("/")` behave the same.
- **Decision:** deleted the test, because it could never fail. The `Location` header test already covered what clients rely on.
- **Know this:** check review findings the same way you check code.

### 14. Reading permission prompts

- **What happened:** the reviewer ran a bash loop (`for f in $(...)`), which can't be checked in advance, and a probe ending in `taskkill //F //IM TicketsApi.exe`, which kills *every* copy of the app.
- **Know this:** read what a command does before approving. Read-only is low risk. Watch for broad kill or delete commands. Edge-case probes done by hand should become permanent tests.

## Part 3: Git and workflow

### 15. Check which branch you're on

- **What happened:** a commit meant for `main` landed on the feature branch, because the branch had been created a moment earlier.
- **Fix:** fast-forward `main` to the same commit (`git merge --ff-only`), so no history was rewritten.
- **Know this:** run `git status` or `git branch --show-current` before committing. `git pull` downloads, and `git push` uploads.

### 16. Line endings on Windows

- **What happened:** Git warned "LF will be replaced by CRLF", and files showed as modified with no real change.
- **Fix:** a `.gitattributes` file (`* text=auto`, and `*.sh text eol=lf`), so the bash hook scripts don't break with `$'\r'` errors.
- **Know this:** teams on mixed operating systems should set line endings in the repo, not per machine.

### 17. From local merge to pull requests

- **What happened:** the first `/finish-ticket` merged locally. We switched to a PR flow: `/create-pr` opens a PR with a description you approve, you merge on GitHub, then `/finish-ticket` cleans up.
- **Know this:** `gh pr merge` is **denied** in the permissions, so the agent can't merge even if it tries. That makes it enforced, not just requested.

## Part 4: The agent team

| Role | Owns | Enforced how |
|---|---|---|
| You | Every product decision | You approve plans, edits and PRs |
| Developer (main session) | Production code | Plan approval, edit approval |
| product-manager | Tickets and requirements | Instructions (edits docs/tickets only) |
| qa-tester | Tests and PASS/FAIL sign-off | Instructions (edits tests only) |
| code-reviewer, security-auditor | Reviews | Read-only tools |
| Commit hook | Build and tests pass | Hook blocks `git commit` |
| teacher | This document | Instructions (edits docs/learning only) |

- **Know this:** be clear which rules are **guaranteed** (hooks, permissions, tool lists) and which are **instructions** an agent could ignore.
- **Interview line:** "Hooks for guarantees, agents for judgement, and a human for decisions."

## Lessons log

New lessons from the teacher agent are added below, newest last.
