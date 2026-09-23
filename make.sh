#!/usr/bin/env bash
# The gate and every platform's publish into artifacts/, built to trust the local
# RELEASE_SIGNING_KEY for updates, as a release made here by release.sh is signed.
set -euo pipefail

cd "$(dirname "$0")"

. ./release-key.sh

public="$(printf '%s\n' "$RELEASE_SIGNING_KEY" | openssl pkey -pubout -outform DER | base64 -w0)"

docker build --build-arg RELEASE_PUBLIC_KEY="$public" --output artifacts .
