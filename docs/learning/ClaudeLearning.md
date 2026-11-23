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

### 18. The Enum.TryParse trap (T-002)

- **What happened:** `GET /tickets?status=...` needed to reject bad values with 400. `Enum.TryParse<TicketStatus>` looked like the obvious tool, but it would have let `?status=99` through, and the request would silently return an empty list instead of a 400.
- **Why:** `Enum.TryParse` is more lenient than it looks. It accepts any number that fits the underlying `int`, even one with no defined name (`"0"`, `"99"`, `"-1"` all succeed). It also trims whitespace (`" Open"` parses) and accepts comma lists (`"Open,Open"`), because enums can be used as flags.
- **Know this:** to accept only real names, compare against the defined values yourself. That's what `TryParseStatus` in `TicketsEndpoints.cs` does:

```
foreach (var candidate in Enum.GetValues<TicketStatus>())
{
    if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
    {
        status = candidate;
        return true;
    }
}
```

- Adding `Enum.IsDefined` after `TryParse` is not enough either (verified by running it on .NET 10). It rejects only undefined numbers like `"99"`. It still accepts `"0"` (0 is the value of `Open`, so it is defined), `" Open"` (trimmed) and `"Open,Open"` (the comma list is ORed together into `Open`). That's why the code matches against the defined names instead. Tests pin each case: `Numeric_status_returns_400` (0, 99, -1), `Unknown_status_name_returns_400` (`"Open,Open"`) and `Status_with_surrounding_spaces_returns_400`. QA swapped in `Enum.TryParse(ignoreCase: true)` and 6 tests failed, which proves the tests guard the rule.
- **Interview line:** "Enum.TryParse accepts undefined numbers, spaces and comma lists, so I never use it on its own for client input. I check against the defined names and write a test for each case."

### 19. Choose a response shape you can grow (T-002)

- **What happened:** the list endpoint returns `{ "items": [...] }` (`TicketListResponse(IReadOnlyList<TicketResponse> Items)`), not a bare JSON array.
- **Why:** paging is planned for T-008. With an object you can add `nextCursor` or `total` later and old clients keep working. Changing a bare array into an object later is a breaking change.
- **Know this:** a public API contract is hard to change once clients depend on it. Leave room for what's already on the roadmap, but don't build it yet. T-008 holds the paging work.
- **Interview line:** "I wrap list results in an object from day one, so adding paging later doesn't break anyone."

### 20. Tests that pass before the code exists (T-002)

- **What happened:** the 404 tests for `GET /tickets/{id}` were green before the endpoint existed, because a missing route also returns 404. The developer checked they still meant something: removing the `{id:guid}` route constraint made 3 tests fail, and adding a `.Take(100)` cap made the 101-ticket test fail. QA ran mutants in a scratch copy and found the GET body test couldn't notice a dropped `description`, because the test never sent one. The fix was to send a multibyte title and description and check both come back unchanged.
- **Why:** "write the test first and watch it fail" breaks down when the failure you expect happens to match the default behaviour.
- **Know this:** if a new test is green from the start, break the code on purpose until it goes red. If nothing makes it red, it isn't testing anything. A test also only checks the data it sends: no description in the request means nothing to compare against.
- **Interview line:** "A test that's green before the code exists makes me suspicious. I mutate the code until it fails, and if it never fails, I fix the test."

### 21. Verify what an agent did, not what it says it did (T-002)

- **What happened:** the product-manager agent reported ticket diffs that looked right. Its bulk edit had actually removed line breaks, merging acceptance criteria 1 and 2, 5 and 6, 7 and 8, and 14 and 15. The code-reviewer caught it by reading the file itself.
- **Why:** an agent's summary describes what it meant to do. A bulk or scripted edit can do something else, and the summary won't say so.
- **Know this:** check the real artifact (`git diff`, the file itself) after an agent edits anything, especially specs, which tests and code are built from. An independent reviewer that reads the source, not the report, is worth having.
- **Interview line:** "I treat an agent's report as a claim. The diff is the evidence, and a second agent reading the file caught what the first one's summary hid."

### 22. Test isolation with an in-memory store and a fake clock (T-002)

- **What happened:** the store is an in-memory singleton, so tickets from one test could show up in another test's list. List order also depends on `createdAt`, which comes from the clock.
- **Fix:** each test builds a fresh host with `WithWebHostBuilder`, so each gets an empty store. A small hand-written `TestClock : TimeProvider` replaces the real clock through `ConfigureTestServices`, so tests set `createdAt` themselves.

```
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => UtcNow;
}
```

- **Know this:** `TimeProvider` is the built-in .NET abstraction for "now". Inject it instead of calling `DateTime.UtcNow`, and tests can control time. Shared state between tests causes flaky, order-dependent failures, so give each test its own.
- **Interview line:** "I inject TimeProvider so tests own the clock, and I give each test its own host so shared in-memory state can't leak between tests."

### 23. Compare-and-swap with ConcurrentDictionary.TryUpdate (T-003)

- **What happened:** the first version of the status change wrote with `dict[key] = value`. Two racing requests both got 200, and the race test failed with Expected [OK, Conflict], Actual [OK, OK]. One update was silently lost.
- **Why:** a read followed by a plain write is not atomic. Between "read the ticket" and "save the ticket", another request can change it, and the second write overwrites the first without noticing.
- **Know this:** `TryUpdate(key, newValue, comparisonValue)` replaces the value only if it is still `comparisonValue`, and returns `false` otherwise. That is compare-and-swap.

```
// InMemoryTicketStore: write only if nobody changed it since we read it
return _tickets.TryUpdate(id, updated, current) ? updated : null;
```

- `Ticket` is an immutable class with no `Equals` override, so the comparison is by reference. "Same reference" means "unchanged since I read it". That even catches A to B to A, where the status looks the same but someone changed it in between.
- In T-005 the same idea becomes an EF Core concurrency token: `UPDATE ... WHERE Status = @expected`, and zero rows updated means you lost the race.
- **Interview line:** "Read-then-write loses updates under concurrency. I use compare-and-swap, TryUpdate in memory and a concurrency token in EF Core, so the loser finds out instead of overwriting."

### 24. Testing a race deterministically (T-003)

- **What happened:** instead of firing 200 parallel rounds and hoping the race shows up, the tests force it every time.
- **Why:** a race that only happens sometimes gives a flaky test. It can pass on your machine and in CI while the bug is still there.
- **Know this:** add a test-only hook that pauses a request at the exact point between its read and its write. Here it is `TicketStoreTestHooks`, an `internal` singleton that the test project can reach through `<InternalsVisibleTo Include="TicketsApi.Tests" />` in the csproj. The tests use a "WriteGate":
1. Arm the gate.
2. Start request X and wait until it is paused after its read.
3. Run competing request Y to completion.
4. Release X. X now always loses, and the test checks it gets the right answer.
- Then prove the test can fail: remove the atomic check and watch it go red.
- **Interview line:** "I don't test races with volume and luck. I add a hook that pauses one request between read and write, run the other one through, and the race happens on every run."

### 25. A test can pass for the wrong reason (T-003)

- **What happened:** the test "loses three times, then 409 with the true current status" ended with the ticket in Resolved, which was also the status the request first read. A bug that reported the stale first read would still pass. QA caught this (finding N6). The fix was a scenario where the final status (InReview) is different from the first read.
- **Why:** the expected value could be produced by both the right and the wrong implementation, so the test could not tell them apart.
- **Know this:** pick test data so that only the correct code can produce the expected value. If two plausible implementations give the same answer, the test is not checking the difference between them.
- **Interview line:** "I ask what a wrong implementation would return. If it's the same as my expected value, the test needs different data."

### 26. Framework binding can run before your checks (T-003)

- **What happened:** the endpoint had to return 415 for a wrong content type. The check was inside the handler, but a broken `application/merge-patch+json` body got 400 instead of 415.
- **Why:** Minimal APIs accept any `+json` content type, and `[FromBody]` parses the body before your handler runs. If parsing fails, the framework answers 400 and your check never runs.
- **Know this:** if your check must come first, take the raw `HttpRequest`, check the content type, then parse yourself:

```
using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
```

- The body size limit still works, because Kestrel throws a `BadHttpRequestException` with 413 while the body is being read.
- Validating the raw `JsonElement` also lets you reject numbers, wrong types and duplicate keys with a 400 that names `status`. With `[FromBody]` and an enum, the default converter would quietly accept `{"status": 1}`.
- **Interview line:** "Model binding runs before the handler, so a check in the handler can come too late. When order matters, I read the raw request and validate the JSON myself."

### 27. Mutants that never ran (T-003)

- **What happened:** twice, the developer's "break the code to prove the test fails" step did nothing, and the result looked like "0 failed":
1. `if (false)` caused an unreachable-code warning. With `TreatWarningsAsErrors` the build failed, so no test ran at all.
2. A regex replacement used `\n`, but the file had CRLF line endings, so it matched nothing and the code was unchanged.
- **Why:** a mutation test only means something if the mutation was applied and compiled. A failed build or a no-op edit looks like a passing run if you only read the last line.
- **Know this:** after mutating, check the diff shows the change and the build succeeded, and check that the test count is what you expect. Only then trust "0 failed". This builds on lessons 11, 16 and 20.
- **Interview line:** "A green run after a mutation proves nothing until I've seen the mutation in the diff and a successful build. Otherwise I might be testing the original code, or nothing."

### 28. Product decisions surface during review (T-003)

- **What happened:** review found that the retry loop had behaviour no acceptance criterion described, and its last step could return a 409 with the wrong current status. The developer did not pick a fix. They asked the product owner, who chose "re-evaluate, then 409 with the true status". The product-manager wrote that down as criterion 25 before the code changed.
- **Why:** edge cases in concurrency are product behaviour: they decide what a client sees. An agent that quietly picks one is making a product decision it doesn't own.
- **Know this:** when a review finds behaviour the spec doesn't cover, stop, ask the decision-maker, record the answer in the ticket, then update tests and code. The order is spec, tests, code.
- **Interview line:** "When agents find a gap in the spec, they propose options and the product owner decides. The decision goes into the acceptance criteria before any code changes."
