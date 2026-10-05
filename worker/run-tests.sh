#!/usr/bin/env bash
# Runs the Worker's tests in the same Node image the gate uses, for a machine without Node.
set -euo pipefail
cd "$(dirname "$0")"
exec docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp -v "$PWD":/w -w /w node:24-bookworm-slim \
  sh -c 'npx tsc --noEmit && npx vitest run "$@"' sh "$@"
