#!/usr/bin/env bash
# Serves the Worker at http://localhost:8787 against a local D1 and R2, in the Node
# image the gate uses, for a machine without Node. Assemble public/ first with
# build-assets.sh. There is no Access here, so admin mode is off.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p public
exec docker run --rm --init -u "$(id -u):$(id -g)" -e HOME=/tmp -e WRANGLER_SEND_METRICS=false -e CI=true \
  -p 8787:8787 -v "$PWD":/w -w /w node:24-bookworm-slim \
  sh -c 'npx wrangler d1 migrations apply flyback-site --local && npx wrangler dev --ip 0.0.0.0 --port 8787'
