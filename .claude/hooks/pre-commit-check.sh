#!/usr/bin/env bash
# PreToolUse hook on Bash. Blocks `git commit` unless the build and all tests pass.
# Claude Code sends the tool call as JSON on stdin. Exit code 2 blocks the command, and stderr is shown to Claude.
input=$(cat)
if ! echo "$input" | grep -qE '"command"[[:space:]]*:[[:space:]]*"[^"]*git[[:space:]]+commit'; then
  exit 0
fi

if ! out=$(dotnet build -nologo 2>&1); then
  echo "Commit blocked: dotnet build failed." >&2
  echo "$out" | tail -n 30 >&2
  exit 2
fi

if ! out=$(dotnet test 2>&1); then
  echo "Commit blocked: dotnet test failed. Fix the failing tests before committing." >&2
  echo "$out" | tail -n 40 >&2
  exit 2
fi

exit 0
