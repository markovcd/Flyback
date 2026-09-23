#!/usr/bin/env bash
# Builds a release into dist/: every platform zipped, the Figures package, and a
# signed SHA256SUMS. The Release workflow runs this and publishes dist/; run here,
# it is the same build signed with a local test key, and nothing is published.
#
#   ./release.sh [version]
#
# version is X.Y.Z. Blank bumps the minor version of the latest vX.Y.Z tag, or
# starts at 0.1.0 with none.
#
# On GitHub, RELEASE_SIGNING_KEY must be the private half of
# src/Flyback.App/Updates/release-key.pem and CHANGELOG.md must have the version's
# heading. Here the key is whatever release-key.sh finds or makes, and a missing
# heading is said but does not stop the build.
set -euo pipefail

cd "$(dirname "$0")"

github=false
[ "${GITHUB_ACTIONS:-}" = true ] && github=true

. ./release-key.sh

temp="$(mktemp -d)"
trap 'rm -rf "$temp"' EXIT

# First, so a bad key fails in seconds rather than after the build.
if ! printf '%s\n' "$RELEASE_SIGNING_KEY" | openssl pkey -pubout -outform DER -out "$temp/derived.der"; then
  echo "release: RELEASE_SIGNING_KEY holds no private key" >&2
  exit 1
fi

# Every copy of Flyback checks updates against the committed public key, so a
# secret that does not pair with it would publish a release that nothing installs.
# Compared as DER, so line endings and wrapping in the PEM make no difference.
if $github; then
  if ! openssl pkey -pubin -in src/Flyback.App/Updates/release-key.pem -outform DER -out "$temp/committed.der" \
     || ! cmp -s "$temp/derived.der" "$temp/committed.der"; then
    echo "release: RELEASE_SIGNING_KEY is not the private half of src/Flyback.App/Updates/release-key.pem" >&2
    exit 1
  fi
fi

input="${1:-}"

if [ -n "$input" ]; then
  version="${input#v}"
  if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "release: the version must be X.Y.Z (got '$input')" >&2
    exit 1
  fi
else
  latest=$(git tag --list 'v[0-9]*.[0-9]*.[0-9]*' --sort=-v:refname | head -n1 || true)
  if [ -z "$latest" ]; then
    version="0.1.0"
  else
    IFS=. read -r major minor _patch <<< "${latest#v}"
    version="${major}.$((minor + 1)).0"
  fi
fi

tag="v${version}"

if git rev-parse "$tag" >/dev/null 2>&1; then
  echo "release: tag $tag already exists" >&2
  exit 1
fi

# The "What's new" window reads a release's notes from its heading, so a build
# whose version has none never shows them (ADR-0088).
if ! grep -qE "^## v?${version//./\\.}([[:space:]]|\$)" CHANGELOG.md; then
  if $github; then
    echo "release: CHANGELOG.md has no '## $version' heading; rename '## Unreleased' before releasing" >&2
    exit 1
  fi
  echo "release: CHANGELOG.md has no '## $version' heading, which a release on GitHub would stop at" >&2
fi

echo "Building $tag"

# Off GitHub the build trusts the local key, so a release made here installs over
# builds made here. On GitHub nothing is passed and the committed key stands.
trust=()
$github || trust=(--build-arg RELEASE_PUBLIC_KEY)

rm -rf dist
docker build --build-arg VERSION="$version" "${trust[@]}" --target release \
  --secret id=release-key,env=RELEASE_SIGNING_KEY --output dist .

# What every copy of Flyback will check it against, before anything is published.
if $github; then
  openssl dgst -sha256 -verify src/Flyback.App/Updates/release-key.pem -signature dist/SHA256SUMS.sig dist/SHA256SUMS
fi

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  echo "version=$version" >> "$GITHUB_OUTPUT"
  echo "tag=$tag" >> "$GITHUB_OUTPUT"
fi

echo "dist/ holds $tag"
ls dist
