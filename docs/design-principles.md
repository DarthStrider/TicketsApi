# Design principles

How we write code in TicketsApi. It combines *Clean Code* (Robert C. Martin) and *A Philosophy of Software Design*
(John Ousterhout). The books agree on most things. Where they disagree, the "Our rule" line says what we do here.

The developer (main session) follows this guide. The `code-reviewer` checks changed code against it.

## The goal: less complexity

Ousterhout defines complexity as anything that makes code hard to understand or change. It shows up as:
- **change amplification:** one change needs edits in many places;
- **cognitive load:** you have to know a lot to make a change safely;
- **unknown unknowns:** it isn't obvious what you need to know.

Every rule below exists to reduce one of these. If following a rule makes the code harder to understand, the rule
is wrong for that case. Say so in the review instead of following it blindly.

## 1. Names

- A name says what something is or does, in the domain's language (`ticket`, `status`, `MaxRequestBodyBytes`).
  Don't use abbreviations or encodings (`tkt`, `strTitle`, `IFooImpl`), or vague words (`data`, `info`, `manager`,
  `helper`, `Process`).
- Names are precise enough that a reader can't guess wrong. `createdAtUtcTicks` beats `time`.
- Use the same word for the same concept everywhere. If a name is hard to choose, the design may be unclear
  (Ousterhout).
- Booleans read as a question: `isValid`, `hasDescription`.

## 2. Explicit types, not `var`

- Write the type: `HttpResponseMessage response = await ...;`, `Ticket ticket = ...;`, `int count = 0;`.
- When the type is already on the right, use target-typed `new()` instead of repeating it:
  `List<Ticket> tickets = new();`, `JsonSerializerOptions options = new() { ... };`.
- **The only exception** is anonymous types, which have no name to write (`var payload = new { title = "t" };`,
  or a LINQ projection to an anonymous type).
- Since T-011 the build enforces this for all code except the generated migrations: a `var` with a named type
  (IDE0008) or a `new Type(...)` that repeats the declared type (IDE0090) fails `dotnet build`, the commit hook and CI.

## 3. Functions and modules: deep, not just small

*Clean Code* says functions should be very small and do one thing. Ousterhout warns that splitting code into many
tiny pieces makes **shallow** modules: lots of interface, little behaviour behind it, so the reader has to jump
around to understand anything.

**Our rule:**
- A function does one thing at one level of abstraction. If you need "and" to describe it, split it.
- Split only when each piece makes sense on its own and has a name that explains it. Don't split just to
  hit a line count. A 30-line function that reads top to bottom is better than five 6-line functions that only
  make sense together.
- Prefer **deep** classes and methods: a simple interface that hides a lot of work (`ITicketStore.TryUpdateStatusAsync`
  hides the version check, the SQL and the retry window). Avoid pass-through methods that only call the next layer
  with the same arguments.
- Few parameters. More than three usually means a missing type. No boolean flag parameters that switch
  behaviour; make two methods instead.
- No hidden side effects: a method called `IsValid` doesn't also trim or save.

## 4. Information hiding

- Each class owns one design decision and hides it (Ousterhout). Example: only `TicketsDbContext` knows that
  `createdAt` is stored as UTC ticks. Nothing outside the Data folder should depend on that.
- Watch for **information leakage**: when two places must change together because they both know the same detail,
  move that detail into one place.
- Pull complexity down: a module should handle its hard cases itself instead of pushing them onto every caller.
- Follow the Law of Demeter (Martin): don't reach through objects (`a.B.C.Do()`). Ask the object you have.
- Keep `internal` and `private` the default. Make something public only when another project needs it.

## 5. Comments: explain why, not what

*Clean Code* treats comments as a failure to express yourself in code. Ousterhout says good comments are part of the
design, because names and types can't say everything.

**Our rule:**
- The code says **what**. If a comment just repeats the code, delete the comment and improve the name.
- Comment the **why** and anything a reader couldn't work out from the code: a framework quirk (the Kestrel race in
  `KestrelFactory.cs`), a ticket rule (`// T-005 criterion 16`), units, ranges, or a non-obvious contract.
- Public interfaces get a comment when their contract isn't obvious from the signature (for example, what
  `TryUpdateStatusAsync` returns on a version conflict).
- A comment that is wrong is worse than none. When you change code, update or delete its comments in the same change.
- No commented-out code, no change logs in comments, and no `// TODO` without a ticket number.

## 6. Errors

- Expected failures (validation, not found, conflict) are not exceptions. They return `TypedResults` with
  `ProblemDetails`, as CLAUDE.md requires.
- Define errors out of existence where you can (Ousterhout): design the API so the error can't happen. Examples:
  treating unknown JSON fields as ignored, or trimming before checking the length.
- Use exceptions for truly unexpected situations. Never swallow them (`catch { }`), and don't catch what you can't handle.
- Don't return `null` for "nothing" when a better shape exists (an empty list, a `Try...` method, a result type).
  When `null` is possible, the nullable annotations must say so; don't silence them with `!`.

## 7. Tests

- Tests follow F.I.R.S.T. (Martin): Fast, Independent (own database per host), Repeatable (no flaky timing),
  Self-validating (asserts, not logs) and Timely (written first, as the workflow requires).
- One concept per test, and the test name states the behaviour (`Title_too_long_returns_400_naming_title`).
- Test code is held to the same standards as production code: clear names, explicit types and no duplication
  that hides intent.

## 8. Formatting and structure

- A file reads top to bottom like a newspaper: the public entry point first, then the helpers it uses.
- Related code sits together: a feature lives in `Features/<Feature>/` (vertical slices).
- Keep the codebase consistent. A new piece of code looks like the code around it.
- `dotnet format` decides layout questions; don't argue about them in reviews.

## 9. Work strategically, within the ticket

- Ousterhout's **strategic programming:** don't just make the ticket pass. Spend a little extra so the design stays clean.
  **Design it twice:** in the plan, mention at least one alternative and why you didn't choose it.
- Martin's **Boy Scout Rule** (leave code cleaner than you found it) applies **only to the code the ticket already
  touches**. Anything bigger is suggested as a new ticket, because CLAUDE.md keeps changes scoped to the ticket.

## Red flags for reviews

These are the main signs that one of the rules above is being broken:

| Red flag | Rule |
|---|---|
| `var` with a named type | 2 |
| A vague or misleading name (`data`, `helper`, `Process`) | 1 |
| A shallow or pass-through method or class | 3, 4 |
| A boolean flag parameter, or more than three parameters | 3 |
| The same detail known in two places (information leakage) | 4 |
| A comment that repeats the code, or a comment that's now wrong | 5 |
| Code that's hard to understand and has no comment explaining why | 5 |
| A swallowed exception, `!` on a nullable, or `null` for "nothing" | 6 |
| A test whose name doesn't state the behaviour, or that tests several concepts | 7 |
| A cleanup outside the lines the ticket touches | 9 |
