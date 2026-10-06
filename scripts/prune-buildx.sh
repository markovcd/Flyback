#!/usr/bin/env bash
# Removes the buildx builders setup-buildx-action left behind, each holding
# gigabytes of layer cache. The action cleans up after its own job but not
# after a cancelled or killed one. Pass the current builder's name to keep it.
set -euo pipefail

keep="${1:-}"

docker buildx ls --format '{{.Name}}' \
  | grep -E '^builder-[0-9a-f-]{36}$' \
  | grep -vxF -- "$keep" \
  | sort -u \
  | while read -r name; do
      echo "removing leaked builder $name"
      docker buildx rm --force "$name"
    done || true
