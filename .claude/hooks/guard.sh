#!/usr/bin/env bash
# PreToolUse guard for the standing rules that a command or an edit can break.
# Exit 2 blocks the call and sends stderr back to the assistant.
set -u
input=$(cat)
tool=$(jq -r '.tool_name // empty' <<<"$input")

deny() { echo "$1" >&2; exit 2; }

case "$tool" in
Bash)
  cmd=$(jq -r '.tool_input.command // empty' <<<"$input")
  # git-workflow.md: stage explicit paths; never sweep up another session's work.
  grep -Eq '(^|[;&|[:space:]])git[[:space:]]+add[[:space:]]+(-A|--all|\.)([[:space:]]|$)' <<<"$cmd" &&
    deny "git add -A / --all / . is blocked (git-workflow.md): stage explicit paths."
  grep -Eq '(^|[;&|[:space:]])git[[:space:]]+commit[^;&|]*[[:space:]](-a|-[a-zA-Z]*a[a-zA-Z]*|--all)([[:space:]]|$)' <<<"$cmd" &&
    ! grep -Eq -- '--amend' <<<"$cmd" &&
    deny "git commit -a is blocked (git-workflow.md): stage explicit paths."
  # A linked worktree is this session's alone; the main checkout is shared.
  if grep -Eq '(^|[;&|[:space:]])git[[:space:]]+stash([[:space:]]+(push|pop|apply|drop|save|clear|branch)|[[:space:]]*($|[;&|]))' <<<"$cmd" &&
    [[ "$(git rev-parse --git-dir 2>/dev/null)" != */worktrees/* ]]; then
    deny "git stash is unsafe in a tree other sessions commit into (git-workflow.md); use a worktree."
  fi
  # pipeline.md: a release is release.sh, never a dispatch from here.
  grep -Eq 'gh[[:space:]]+workflow[[:space:]]+run|gh[[:space:]]+api[^;&|]*/dispatches' <<<"$cmd" &&
    deny "Release dispatch waits for the user's go (pipeline skill)."
  ;;
Edit|Write)
  path=$(jq -r '.tool_input.file_path // empty' <<<"$input")
  case "$path" in
  */PublicAPI.Shipped.txt)
    deny "PublicAPI.Shipped.txt is never edited; use a *REMOVED* line in PublicAPI.Unshipped.txt (plugin-contract skill)." ;;
  */release-key.pem)
    deny "release-key.pem is the committed public key; changing it is the user's decision (security.md)." ;;
  esac
  # Rules kept as skills: name the one that binds this file, once per session.
  skill=""
  case "$path" in
  */.github/workflows/* | */Dockerfile | */global.json | */.github/dependabot.yml | */scripts/gate.sh | */scripts/release.sh | */scripts/builder.sh)
    skill=pipeline ;;
  */PublicAPI.Unshipped.txt | */src/Flyback.Core/* | */src/Flyback.Plugins/*)
    skill=plugin-contract ;;
  */PatchIO*.cs | */Graph/layouts/* | */sockets.txt | */PresetStore*.cs)
    skill=saved-data ;;
  esac
  if [[ -n "$skill" ]]; then
    session=$(jq -r '.session_id // "none"' <<<"$input")
    mark="${TMPDIR:-/tmp}/flyback-skill-${session//[^A-Za-z0-9_-]/}-$skill"
    if [[ ! -e "$mark" ]]; then
      : >"$mark" 2>/dev/null
      jq -n --arg s "$skill" '{hookSpecificOutput: {hookEventName: "PreToolUse",
        additionalContext: "This file is bound by the \($s) skill; load it before writing here if it is not loaded."}}'
    fi
  fi
  ;;
esac
exit 0
