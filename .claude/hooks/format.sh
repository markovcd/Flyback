#!/usr/bin/env bash
# PostToolUse: apply .editorconfig's whitespace rules to the C# file just written,
# so the gate's `dotnet format whitespace --verify-no-changes` finds nothing.
set -u
path=$(jq -r '.tool_input.file_path // empty')
[[ "$path" == *.cs ]] || exit 0
command -v dotnet >/dev/null || exit 0
cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0
dotnet format whitespace Flyback.slnx --include "$path" --no-restore >/dev/null 2>&1
exit 0
