#!/usr/bin/env bash
# PreToolUse hook. Claude Code sends the tool call as JSON on stdin.
# Exit code 2 blocks the edit, and the message on stderr is shown to Claude.
input=$(cat)
if echo "$input" | grep -qE '"file_path"[[:space:]]*:[[:space:]]*"[^"]*(\.env|appsettings\.Production\.json|\.github/workflows/)'; then
  echo "Blocked: this file is protected. Ask the developer to change it." >&2
  exit 2
fi
exit 0
