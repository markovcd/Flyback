#!/usr/bin/env bash
# Builds the site here and starts it on the NAS over ssh. See README.md beside this file.
#
#   deploy/site/deploy.sh [host] [folder]
#
# host defaults to nas and folder to flyback-site, relative to the remote home.
# DOCKER overrides the remote docker command, for a NAS that wants "sudo docker".
# RELEASE_SIGNING_KEY signs the plugins the site starts with, as it signs a
# release; release-key.sh finds it or makes a local test key. The build takes it
# as a secret and the image keeps no copy.
set -euo pipefail

host="${1:-nas}"
dir="${2:-flyback-site}"
docker="${DOCKER:-docker}"
image=flyback-site
cd "$(dirname "$0")/../.."

. ./release-key.sh

arch="$(ssh "$host" uname -m)"
case "$arch" in
  x86_64|amd64) platform=linux/amd64 ;;
  aarch64|arm64) platform=linux/arm64 ;;
  *) echo "deploy: no image for $host's $arch" >&2; exit 1 ;;
esac

echo "Building $image for $platform"
docker build --platform "$platform" --secret id=release-key,env=RELEASE_SIGNING_KEY -f src/Flyback.Server/Dockerfile -t "$image" .

echo "Loading $image on $host"
docker save "$image" | gzip | ssh "$host" "gunzip | $docker load"

# compose.yaml is copied only once: the NAS copy holds the admin's password.
ssh "$host" "mkdir -p '$dir/media' && test -e '$dir/compose.yaml'" \
  || scp deploy/site/compose.yaml "$host:$dir/compose.yaml"

# The container runs as user 1654 and writes data/.
if ! ssh "$host" "test -d '$dir/data'"; then
  ssh -t "$host" "mkdir -p '$dir/data' && sudo chown 1654 '$dir/data'"
fi

echo "Starting $image on $host"
ssh "$host" "cd '$dir' && $docker compose up -d --force-recreate"
