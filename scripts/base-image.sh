#!/usr/bin/env bash
# Prints the image a Dockerfile stage starts from, digest and all, so whatever runs
# that image outside the build runs the one Dependabot moves.
#
#   scripts/base-image.sh sdk|worker
set -euo pipefail
stage="${1:?usage: base-image.sh <stage>}"
image="$(sed -n "s/^FROM \([^ ]*:[^ ]*\) AS ${stage}\$/\1/p" "$(dirname "$0")/../Dockerfile")"
if [ -z "$image" ]; then
  echo "base-image: the Dockerfile has no 'FROM <image> AS ${stage}' line" >&2
  exit 1
fi
echo "$image"
