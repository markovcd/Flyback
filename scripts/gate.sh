#!/usr/bin/env bash
# The gate: restore, compile and every test, in the image the release is built from.
# The Build workflow runs this and so can anyone.
#
#   ./scripts/gate.sh
set -euo pipefail

cd "$(dirname "$0")/.."

. ./scripts/builder.sh

# Only GitHub's runners write the shared cache; a self-hosted one has its own.
[ "${#cache[@]}" -gt 0 ] && cache+=(--cache-to type=gha,mode=max)

docker buildx build "${builder[@]}" "${cache[@]}" --target gate .
