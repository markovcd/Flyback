#!/usr/bin/env bash
# Runs the Worker's tests in the same Node image the gate uses, for a machine without Node.
set -euo pipefail
cd "$(dirname "$0")"
exec docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp -v "$PWD":/w -w /w node:26.11.1-bookworm-slim@sha256:86f07bc9c5dce4578cf37e5a418b7bfc7f817cda25cde66e2b66e95ed86c4567 \
  sh -c 'npx tsc --noEmit && npx vitest run "$@"' sh "$@"
