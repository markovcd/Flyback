#!/usr/bin/env bash
# The gate and every platform's publish into artifacts/, built to trust the local
# RELEASE_SIGNING_KEY for updates, as a release made here by release.sh is signed.
set -euo pipefail

cd "$(dirname "$0")"

. ./release-key.sh

docker build --build-arg RELEASE_PUBLIC_KEY --output artifacts .
