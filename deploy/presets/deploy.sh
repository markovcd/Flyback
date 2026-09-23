#!/usr/bin/env bash
# Builds the preset site here and starts it on the NAS over ssh. See README.md beside this file.
#
#   deploy/presets/deploy.sh [host] [folder]
#
# host defaults to nas and folder to flyback-presets, relative to the remote home.
# DOCKER overrides the remote docker command, for a NAS that wants "sudo docker".
set -euo pipefail

host="${1:-nas}"
dir="${2:-flyback-presets}"
docker="${DOCKER:-docker}"
image=flyback-presets

cd "$(dirname "$0")/../.."

arch="$(ssh "$host" uname -m)"
case "$arch" in
  x86_64|amd64) platform=linux/amd64 ;;
  aarch64|arm64) platform=linux/arm64 ;;
  *) echo "deploy: no image for $host's $arch" >&2; exit 1 ;;
esac

echo "Building $image for $platform"
docker build --platform "$platform" -f src/Flyback.Server/Dockerfile -t "$image" .

echo "Loading $image on $host"
docker save "$image" | gzip | ssh "$host" "gunzip | $docker load"

# compose.yaml is copied only once: the NAS copy holds the admin's password.
ssh "$host" "mkdir -p '$dir/media' && test -e '$dir/compose.yaml'" \
  || scp deploy/presets/compose.yaml "$host:$dir/compose.yaml"

# The container runs as user 1654 and writes data/.
if ! ssh "$host" "test -d '$dir/data'"; then
  ssh -t "$host" "mkdir -p '$dir/data' && sudo chown 1654 '$dir/data'"
fi

echo "Starting $image on $host"
ssh "$host" "cd '$dir' && $docker compose up -d --force-recreate"
