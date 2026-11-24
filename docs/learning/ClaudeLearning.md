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

### 29. Middleware order decides what gets localized (T-004)

- **What happened:** `UseRequestLocalization` runs first in `Program.cs`, before `UseExceptionHandler` and `UseStatusCodePages`. QA moved it after them as a mutant and 14 tests failed: the French titles for 413, 500, the non-GUID 404, 405 and 415 came back in English.
- **Why:** middleware is a set of nested calls. Each one wraps everything registered after it. The exception handler and status-code pages write their response *after* the inner pipeline returns. The culture is stored per async flow, and a value set inside an inner call does not flow back out to the caller, so the outer error handlers still see the default culture.
- **Know this:** register anything that sets up per-request context (culture, correlation ids) before the middleware that writes responses using it.

```
app.UseTicketsLocalization();   // sets the culture first
app.UseExceptionHandler(...);   // its ProblemDetails is now localized
app.UseStatusCodePages();
```

- **Interview line:** "Middleware wraps whatever comes after it, so context set further in isn't visible to the error handlers outside. Localization goes first, and a mutant that moves it proves the order matters."

### 30. The exception handler clears response headers (T-004)

- **What happened:** `Content-Language` and `Vary: Accept-Language` must be on every response. Set directly, they would disappear on the 413 and 500 responses. QA's first tests missed it: a "set Vary directly" mutant passed every test until the tests also checked `Vary` on responses written by the exception handler (the PATCH 413 and the 500).
- **Why:** before writing its ProblemDetails, the exception handler clears the response, including its headers. Callbacks registered with `Response.OnStarting` are not cleared, and they run just before the headers are sent.
- **Know this:**

```
context.Response.OnStarting(() =>
{
    context.Response.Headers.ContentLanguage = language;
    context.Response.Headers.Append("Vary", "Accept-Language");
    return Task.CompletedTask;
});
```

- A header test only covers the code paths it calls. If a response can come from several places (the endpoint, status-code pages, the exception handler), test at least one of each.
- **Interview line:** "The exception handler resets headers, so headers that must be on every response go in an OnStarting callback, and I test them on the exception path too, not just the happy path."

### 31. UI culture vs formatting culture (T-004)

- **What happened:** the security audit noted that if `Accept-Language: fr` also set `CurrentCulture`, number, date and string formatting would be under the client's control. That could break a later parse or change something like a rate-limit key. The fix keeps formatting in English and lets only the message language change. QA pinned it with a test that records both cultures inside a real request.
- **Why:** .NET has two culture settings per thread. `CultureInfo.CurrentUICulture` picks which resource file (language) is used. `CultureInfo.CurrentCulture` drives `ToString`, parsing and comparison (for example `1.5` vs `1,5`).
- **Know this:**

```
options.SupportedCultures   = [en];      // CurrentCulture: always English
options.SupportedUICultures = [en, fr];  // CurrentUICulture: from Accept-Language
```

- **Interview line:** "I let the client choose the language of the messages, not how my server formats and parses data. Only the UI culture follows Accept-Language."

### 32. Don't compare a value with itself (T-004)

- **What happened:** the tests compare responses against a copy of the approved EN/FR table from the ticket (`Texts.cs`), not against the `.resx` files the code reads. A separate key-parity test reads the French resource set with `tryParents: false`.
- **Why:** if the test read the same `.resx` as the code, it would only prove the wiring. A wrong translation would appear on both sides and pass. And a missing French key doesn't fail at runtime: .NET silently falls back to the English (parent) resource.
- **Know this:** the expected value in a test must come from an independent source, here the spec. `GetResourceSet(culture, createIfNotExists: true, tryParents: false)` returns only the entries really in `Messages.fr.resx`, so a missing one shows up instead of being filled in from English.
- **Interview line:** "Expected values come from the spec, not from the code under test, otherwise the test can't disagree with it. And I check that every translation key exists, because resource fallback hides missing ones."

### 33. Fixed text can drift from the code (T-004)

- **What happened:** messages like "Status must be one of: Open, ..." and "at most 200 characters" are now literal text in the resource files. The code reviewer pointed out that adding a status or changing a length limit would leave the messages silently wrong. A test now checks that the text contains every name from `Enum.GetNames<TicketStatus>()` and the length constants.
- **Why:** moving text out of code into resources breaks the link the compiler used to give you. Nothing fails when the enum or the constant changes.
- **Know this:** when a value is copied into text, write a test that ties the text back to its source in code.
- **Interview line:** "Once a number or an enum list lives in a resource string, the compiler no longer protects it, so I add a test that ties the text to the constant."

### 34. Green before the code isn't proof of value (T-004)

- **What happened:** the framework's English messages already matched the ticket, so the English test cases passed before any localization code existed. They were proven useful with a mutant: setting the default culture to `fr` made 40 tests fail.
- **Why:** when the expected value happens to equal the default behaviour, "write the test, watch it fail" never shows the failure. This is the same trap as lesson 20, in a new form.
- **Know this:** a test earns trust when a realistic bug turns it red. If it is green from the start, make a mutant that would break the behaviour and confirm the test catches it (and, per lesson 27, confirm the mutant was really built and run).
- **Interview line:** "A test that was green from day one hasn't proven anything yet. I break the code on purpose, and only a red result tells me the test is worth keeping."

### 35. Check the framework before adding a package (T-004)

- **What happened:** localization (`AddLocalization`, `UseRequestLocalization`, `IStringLocalizer`) needed no NuGet package. It is part of the ASP.NET Core shared framework that a web project already references.
- **Why:** many features that used to be separate packages now ship in `Microsoft.AspNetCore.App`. Adding a package for them is unnecessary and can add a version to keep in sync.
- **Know this:** before adding a package, check whether the API is already in the shared framework. This is part of the rule "never write a package version from memory": the best version to pick is often no package at all.
- **Interview line:** "Before adding a dependency I check whether the framework already has it. For localization in ASP.NET Core it does, so there's nothing to install or upgrade."

### 36. A version number, not the status, as the concurrency token (T-005)

- **What happened:** the status change is an optimistic check-and-set: read the ticket, then write only if nothing changed. Criterion 8 made an integer `Version` the concurrency token, not `Status`.
- **Why:** if the write checked "status is still A", a change A -> B -> A by other requests in between would pass the check, and the change made in between would be silently overwritten. A number that goes up on every write never comes back to an old value, so any write in between makes the check fail.
- **Know this:** in `EfTicketStore`, the write only matches the row if the version is the one that was read, and bumps it in the same statement. Zero rows updated means someone else wrote first, and the change is re-evaluated (T-003 criterion 25):

```
var updated = await db.Tickets
    .Where(t => t.Id == id && EF.Property<int>(t, "Version") == version)
    .ExecuteUpdateAsync(s => s
        .SetProperty(t => t.Status, next)
        .SetProperty(t => EF.Property<int>(t, "Version"), version + 1), ct);
```

- `Version` is a *shadow property*: it exists in the EF model and the table but not on the `Ticket` class, so it can never leak into a response (criterion 10).
- **Interview line:** "I use a version number as the concurrency token, never a business field. A field can go from A to B and back to A, and then the check passes and an update is lost."

### 37. SQLite and DateTimeOffset: store UTC ticks (T-005)

- **What happened:** `GET /tickets` must be sorted newest first by the database, not in memory (criterion 5). The EF Core SQLite provider can't translate `ORDER BY` on a `DateTimeOffset` column, so `createdAt` is stored as UTC ticks in an INTEGER column through a value converter.
- **Why:** SQLite has no real date type. EF Core stores a `DateTimeOffset` as text there, and text with offsets doesn't sort as time, so the provider refuses to translate the sort.
- **Know this:** a value converter maps a .NET type to a column type. One tick is 100 ns, so the value also round-trips exactly, to the tick (criterion 4):

```
ticket.Property(t => t.CreatedAt).HasConversion(
    value => value.UtcTicks,
    ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
```

- The trade-off: the original offset is not stored and always comes back as `+00:00`. That is fine here because the app only writes UTC.
- **Interview line:** "SQLite can't sort a DateTimeOffset, so I store UTC ticks as an integer with a value converter. It sorts in SQL and keeps full precision."

### 38. One real SQLite file per test host (T-005)

- **What happened:** `TicketsApiFactory` gives every test host its own database: a temp file with a unique name (`tickets-test-<guid>.db`), migrated before the host starts. The tests run against real SQLite, not the EF Core InMemory provider (criterion 17).
- **Why:** a shared database makes tests depend on each other and on the order they run in. The InMemory provider is not a relational database and runs no SQL, so it can't prove the SQL sort, the `ExecuteUpdate` concurrency check or the migrations. Microsoft's own docs advise against it for this kind of testing.
- **Know this:** a unique file per host gives isolation, so tests can run in parallel, and keeps real database behaviour. On Windows, SQLite's connection pool keeps the file open, so cleanup calls `SqliteConnection.ClearAllPools()` before deleting the file and its `-wal` and `-shm` side files.
- **Interview line:** "My integration tests use the real database engine, one fresh file per test host. They stay isolated and parallel, and they test the SQL I actually ship."

### 39. Migrate() refuses pending model changes (T-005)

- **What happened:** since EF Core 9, `Migrate()` throws when the model has changes that no migration covers. A model change without `dotnet ef migrations add` fails at startup instead of running against an old schema. Criterion 14 also has a test that asks EF Core whether the model has pending changes.
- **Why:** before EF Core 9 it was easy to change an entity, forget the migration, and only find out when a query hit a missing column.
- **Know this:** the model (your C# configuration) and the migrations (the committed history of schema changes) must agree. After every model change, add a migration, and keep a test that fails when they drift. Outside Development this API doesn't migrate at startup: it refuses to start if migrations are pending (criterion 20), because they are applied before deploy with `dotnet ef database update` or a migration bundle.
- **Interview line:** "Changing the model and adding the migration are one step. EF Core 9 enforces it in Migrate, and a test catches it even earlier."

### 40. SQLite doesn't enforce max lengths (T-005)

- **What happened:** the model declares `HasMaxLength(200)` for the title and `HasMaxLength(4000)` for the description, as documentation only (criterion 3). An earlier draft of this lesson said the pending-changes check from lesson 39 might still ask for a migration after a max-length change. The developer tested that in a throwaway copy of the repo: after changing the title to `HasMaxLength(300)`, `dotnet ef migrations has-pending-model-changes` printed "No changes have been made to the model since the last migration". A forced `dotnet ef migrations add TitleLength` had an empty `Up()`, and its SQL script only inserted the `__EFMigrationsHistory` row, with no ALTER and no table rebuild.
- **Why:** SQLite uses type affinity: a declared length such as `TEXT(200)` is accepted but never checked. EF Core's SQLite provider ignores max length when it compares the model with the last migration, even though InitialCreate records `maxLength: 200`. Server databases such as SQL Server or PostgreSQL do enforce lengths.
- **Know this:** on SQLite, a max-length-only change needs no migration, and neither `HasPendingModelChanges()` nor the `Model_has_no_pending_changes` test can catch one. The endpoint validation (T-001) is the only enforcement. If the database moves to a server engine, the lengths become real limits and real migrations. The meta-point is lesson 42 again: a plausible but untested claim was checked with a cheap experiment before it stayed in the notes.
- **Interview line:** "On SQLite, a max length is documentation, and EF Core doesn't even see a length change as a model change. I proved that in a throwaway copy rather than trusting a plausible guess. Validation lives at the API boundary."

### 41. "Success" against the wrong database (T-005)

- **What happened:** `dotnet ef database update` without `--connection` reported success. The design-time factory named no database, so SQLite opened an empty temporary database, migrated it and threw it away. Nothing real changed. The factory now uses `Mode=ReadOnly`, so the same command fails and `--connection "<connection string>"` is required.
- **Why:** with SQLite, an empty data source is valid: it means a temporary database. So "no database configured" was not an error, it was a throwaway target.
- **Know this:** `IDesignTimeDbContextFactory<T>` is the class the `dotnet ef` tool uses to build your context without starting the app. Make its default fail loudly rather than succeed against nothing:

```
public TicketsDbContext CreateDbContext(string[] args) =>
    new(new DbContextOptionsBuilder<TicketsDbContext>().UseSqlite("Mode=ReadOnly").Options);
```

- **Interview line:** "A command that succeeds against the wrong target is worse than one that fails. I made the tool's default read-only, so applying migrations without naming the database is an error."

### 42. Check a reviewer's claim before acting on it (T-005)

- **What happened:** in T-005, review findings were checked against the code or reproduced before anything changed. This is the habit from lesson 13: in T-001 the reviewer's trailing-slash claim turned out to be wrong.
- **Why:** a reviewer agent can sound certain and still be wrong. Acting on a wrong finding adds code, tests or scope for nothing.
- **Know this:** treat each finding as a hypothesis. Reproduce it (a test, a probe, or reading the exact code), then fix it, reject it with a reason, or move it to a new ticket.
- **Interview line:** "Review findings are hypotheses. I reproduce them or ask for evidence before anyone changes code."

### 43. The 413 flake: amplify a rare race to prove the fix (T-005)

- **What happened:** the 413 (body too large) tests on Kestrel sometimes failed with a connection reset instead of a 413. Kestrel knew from `Content-Length` that the body was too large, answered while the client was still uploading, and closed the connection. On Windows the unread data causes a reset, and the client sometimes saw the reset instead of the response.
- **Fix:** the tests send `Expect: 100-continue` (`OverCapRequests`). The client sends only the headers and waits. Kestrel rejects the request before asking for the body, so the body is never sent and there is nothing to race. If the limit were missing, Kestrel would ask for the body and the test would fail, as it should.
- **Proof:** 20 runs could not reproduce the flake. Making it worse on purpose (a 4 MB body under load) gave 352 failures out of 600 before the fix and 0 out of 600 after.
- **Know this:** a rare race can't be proven fixed by "it passed 20 times". Change the conditions so the bug happens often, measure, then apply the fix under the same conditions. It's the same idea as lesson 24: make the race happen on purpose.
- **Interview line:** "When a flake won't reproduce, I amplify it until it fails often, then show the fix takes it to zero under the same load. 352 of 600 down to 0 of 600 is proof; a few green runs are not."

### 44. A mutant on a stale build (T-005)

- **What happened:** QA's mutant runner swapped files in and out. Restored files kept their old timestamps, so the build could treat the project as up to date and the tests ran against an old dll. A mutant "passed" without its change ever being compiled.
- **Why:** incremental builds decide what to rebuild from file timestamps. A file with an old timestamp can look unchanged even when its content is different.
- **Know this:** this extends lesson 27. A mutant must change something that actually gets rebuilt, or its result means nothing. Force the rebuild (touch the file or use `dotnet build --no-incremental`) and confirm that a known mutant turns the expected tests red before trusting any green result.
- **Interview line:** "A mutant only counts if it was really compiled. I check the build picked up the change, because a stale binary makes every mutant look harmless."

### 45. A criterion the tests only nearly prove is a gap (T-005)

- **What happened:** criterion 16 said each test database is "deleted when the host is disposed", but a host derived with `WithWebHostBuilder` or `WithClock` only had its file deleted when the class-fixture factory was disposed, at the end of the test class. The product owner chose to change the behaviour to match the wording rather than weaken it: `TicketsApiFactory` now overrides `CreateHost` and wraps each host it owns in `DatabaseOwningHost`, so the file (plus its `-wal` and `-shm` files) is deleted as soon as that host is disposed. Files a test chooses itself (`FixedDatabaseFactory`) are never deleted by a host. The product-manager recorded this in the ticket.
- **Why:** the tests proved something close to the criterion, not the criterion itself, and nobody had decided whether "end of the class" was good enough. The existing end-of-class tests still passed with the per-host deletion removed, so new tests were needed to prove the "when the host is disposed" part.
- **Know this:** when tests only approximately prove a criterion, raise it as a decision: change the code to match the text, or change the text (through the product-manager) to match the code. Either can be right, but the product owner chooses, and the choice is written down. A side note on the fix: `WebApplicationFactory` was always `IDisposable` and the tests already used `using`; what changed is what disposing a host does. The hook is `CreateHost` because `WithWebHostBuilderCore` is internal in Mvc.Testing 10.0.15 and can't be overridden.
- **Interview line:** "If the tests only nearly prove a criterion, that's a gap to resolve with the product owner, not something to paper over. Either the code or the spec changes, on purpose."

### 46. A derived test host silently drops Kestrel (T-005)

- **What happened:** in Mvc.Testing 10.0.15, `kestrelFactory.WithWebHostBuilder(...)` built its host on the in-memory `TestServer`, because `UseKestrel` isn't inherited by the derived factory. Limits that only real Kestrel enforces, such as the maximum request body size, silently stopped applying. Worse, the derived factory's `CreateClient()` still reported the root host's real port in `BaseAddress`, so a "has a real port" check was fooled. Even after calling `derived.UseKestrel(0)`, `derived.CreateClient()` still targeted the root's port, so the requests wrote into the root host's database, not the derived one's.
- **Why:** a derived factory is a new host with its own server, but some settings live on the root factory object and are not copied over, and the client helper does not tell you which server it actually talks to. The tests passed, but against the wrong server and the wrong database. The database-engineer found this by probing; the code reviewer had only suspected it.
- **Know this:** the working recipe is to make the derived host use Kestrel, start it, and build the client from that host's own address, then assert what you are really running against:

```
var derived = factory.WithWebHostBuilder(_ => { });
derived.UseKestrel(); // not inherited; no port (UseKestrel(0) races the app's start, see lesson 48)

// TestDatabaseCleanupTests.CreateKestrelClient
static HttpClient CreateKestrelClient(WebApplicationFactory<Program> app)
{
    app.StartServer();
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
    return app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(Assert.Single(address ?? [])) });
}
```

> Correction: this recipe originally used `derived.UseKestrel(0)`. It was changed to `derived.UseKestrel()` after the start race described in lesson 48.

- In `tests/TicketsApi.Tests/Data/TestDatabaseCleanupTests.cs`, `AssertRunsOnKestrel` then checks that the `IServer` is not `TestServer`, that its type name starts with "Kestrel", and that the server listens on the client's port. `AssertStoredInAsync` checks that the ticket landed in that host's own database file.

- The general point: a green test only tells you something if it ran against what you think it ran against. Check the server type and where the data went, not only the status code.
- **Interview line:** "A test passing isn't the same as a test testing the right thing. When a derived test host silently fell back to the in-memory server, we caught it by asserting the server type and which database got the write, not by trusting green."

### 47. Verify an agent's "nothing left over" claim yourself (T-005)

- **What happened:** the database-engineer reported 0 leftover `tickets-test-*.db` files before and after its work. The developer's own check found 32. Every one was timestamped during the agent's mutant runs, where deleting the files had been deliberately switched off. The developer moved them aside and ran the suite with a fresh before/after count: 0 and 0. That proved the real code doesn't leak files; only the experiments did.
- **Why:** the agent's count didn't cover its own experiments. Mutant runs that turn off cleanup leave debris by design, and nobody had made the experiments clean up after themselves.
- **Know this:** a verification an agent reports is itself a claim, and needs the same treatment as a review finding (lesson 42). Be extra careful after an agent has run destructive or mutating experiments, which can leave state behind just like a stale build can (lesson 44). Give experiments their own cleanup, and when you count, separate "left by the real code" from "left by the experiment" (here the timestamps did that).
- **Interview line:** "When an agent says 'verified, nothing left over', I re-run the check myself. Here its 0 was really 32, all from its own experiments; a clean before/after run showed the product code was fine."

### 48. A CI failure that only happened once: the Kestrel start race (PR #7)

- **What happened:** CI on main failed once after PR #5 merged (run 45254524742, attempt 1). The PR's own run had passed on the same code, and attempt 2 was green. The error was `IServerAddressesFeature.Addresses cannot be modified after the server has started`, thrown from `WebApplicationFactory`'s `TryConfigureServerPort` inside `StartServer()`.
- **Why:** reading the release/10.0 source showed the call order. `UseKestrel(0)` sets a port, so `CreateHost` runs `Build()`, then `Addresses.Clear()` and `Add`, then `Start()`. With a top-level `Program.cs`, the app's own `app.Run()` starts Kestrel on the entry-point thread. When that thread wins, `Clear()` throws because the server has already started. The call order is confirmed from the source; the threading part was inferred and then supported by reproduction.
- **Know this:**
  - Before blaming a merge, compare the PR's own run with the run on main. PR #5 only added Markdown, so the merge could not have caused it.
  - Read the framework's source instead of guessing. It is public on GitHub, and you can pick the branch that matches your version.
  - Make a rare failure happen often to prove a fix (the same idea as lesson 43). QA's stress loop of 32 parallel starts gave 69 failures in 18,000 (about 0.4%), plus an `ObjectDisposedException` from the same method. Run one at a time, it never failed. After the fix: 0 in 12,000.
  - The fix was to stop passing a port to Kestrel and give the address through URLs instead:

```csharp
// before (in the factory's constructor): a port makes the factory
// edit Addresses between Build() and Start()
UseKestrel(0);

// after (tests/TicketsApi.Tests/Tickets/KestrelFactory.cs, abridged)
public KestrelFactory() => UseKestrel();   // no port

protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    base.ConfigureWebHost(builder);
    builder
        .UseUrls("http://127.0.0.1:0")      // port 0 = any free port
        .UseSetting(WebHostDefaults.PreferHostingUrlsKey, "true");
}
```

  - Put an infrastructure fix that affects main on its own branch (PR #7), not inside a feature ticket. T-005 then took the fix through a merge and removed its own two `UseKestrel(0)` calls.
  - Rerunning the job from the CLI was blocked because the fine-grained token had no Actions write permission. The user reran it from the GitHub page instead of widening the token: least privilege beats convenience.
  - The episode led to a read-only `devops-engineer` agent (PR #6) for CI investigations.
- **Interview line:** "A test that fails once in 260 runs is still a bug. I compared runs to rule out the merge, read the framework source to find the race, reproduced it under parallel load, and fixed the cause, not the symptom."
