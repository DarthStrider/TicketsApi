# TicketsApi: learning project for AI-directed .NET development

A small ASP.NET Core API that you build **by directing Claude Code**, not by typing the code yourself.
The repo comes already set up with everything the Maskatel posting asks about:

| Posting requirement | Where it lives here |
|---|---|
| Repository-level instructions (CLAUDE.md) | `CLAUDE.md` |
| Reusable agent skills (SKILL.md) | `.claude/skills/add-endpoint/SKILL.md` |
| Hooks | `.claude/settings.json` + `.claude/hooks/protect-files.sh` |
| Permissions | `.claude/settings.json` (allow / ask / deny) |
| Subagents | `.claude/agents/code-reviewer.md`, `security-auditor.md` |
| Requirements → acceptance criteria | `docs/tickets/T-001` … `T-006` |
| Automated tests | `tests/TicketsApi.Tests` (xUnit + WebApplicationFactory) |
| CI/CD | `.github/workflows/ci.yml` (GitHub Actions) |

## Setup
1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), Git, and [Claude Code](https://code.claude.com/docs).
2. `git init && git add . && git commit -m "Initial scaffold"`
3. `dotnet test`: one passing test (`/health`). Open `TicketsApi.slnx` in Visual Studio or Rider.
4. Push to a new GitHub repo and watch the CI run.

## Built to grow
- `TicketsApi.slnx`: the solution file. Add projects with `dotnet sln add <path>`.
- `Directory.Build.props`: shared settings (.NET 10, nullable, warnings as errors), which every new project inherits.
- `Directory.Packages.props`: all NuGet versions in one place (central package management).
- Features go in `src/TicketsApi/Features/<Feature>/` (vertical slices), so new features don't touch old ones.
- When the API outgrows one project, split it into `src/TicketsApi.Domain`, `src/TicketsApi.Infrastructure`, etc. under `src/`, with tests mirrored under `tests/`.

## How to work each ticket
1. `git checkout -b feature/T-001-create-ticket`
2. Start `claude` and use **plan mode** (Shift+Tab): *"Read docs/tickets/T-001-create-ticket.md. Explore the code and propose a plan."*
3. Review the plan and push back on it. Then: *"Write the tests first and show me they fail."*
4. *"Now implement using the add-endpoint skill."*
5. *"Use the code-reviewer subagent on the diff."* Fix what matters.
6. Read every line yourself. Commit, push and open a PR, and let CI pass.
7. **Improve the system:** if the agent made a mistake twice, add a rule to CLAUDE.md or the skill.

Keep a `LEARNINGS.md` of what went wrong and what you changed. Those are your interview stories.

Note: these files were written without a .NET SDK available to compile them, so run `dotnet restore` and fix
any package version drift (a good first task to give Claude Code).
