#!/usr/bin/env bash
# Runs the Worker's tests in the same Node image the gate uses, for a machine without Node.
set -euo pipefail
cd "$(dirname "$0")"
exec docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp -v "$PWD":/w -w /w node:26.10.0-bookworm-slim@sha256:3ffc19ea878019d9e9ae8971732ad4a03cda44f167107173174b60ed7c65bed3 \
  sh -c 'npx tsc --noEmit && npx vitest run "$@"' sh "$@"
