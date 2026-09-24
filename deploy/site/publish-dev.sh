#!/usr/bin/env bash
# Builds the site from this checkout and pushes it as ghcr.io/markovcd/flyback-site:dev,
# which the NAS's dev instance pulls. See README.md beside this file.
#
#   deploy/site/publish-dev.sh
#
# Needs a one-time `docker login ghcr.io` with a token that may write packages.
# The plugins the site starts with are signed with RELEASE_SIGNING_KEY, which
# here is the local test key, so only a local build of Flyback installs them.
set -euo pipefail

cd "$(dirname "$0")/../.."

. ./release-key.sh

image=ghcr.io/markovcd/flyback-site
commit="$(git rev-parse --short HEAD)"
git diff --quiet HEAD || commit="$commit-dirty"

echo "Building $image:dev ($commit)"
docker build --platform linux/amd64 --secret id=release-key,env=RELEASE_SIGNING_KEY \
  -f src/Flyback.Server/Dockerfile -t "$image:dev" -t "$image:dev-$commit" .

docker push "$image:dev"
docker push "$image:dev-$commit"

echo "On the NAS, in the dev folder: docker compose pull && docker compose up -d"
