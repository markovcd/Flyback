#!/usr/bin/env bash
# Runs the Worker's tests in the same Node image the gate uses, for a machine without Node.
set -euo pipefail
cd "$(dirname "$0")"
exec docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp -v "$PWD":/w -w /w node:24.21.0-bookworm-slim@sha256:d6aa754f16b3197301076f047b5def2f02ea1dbbc2ca920407d46d7ec7f87b20 \
  sh -c 'npx tsc --noEmit && npx vitest run "$@"' sh "$@"
