#!/usr/bin/env bash
# Installs the Hugo that builds the website (ADR-0222) into a folder, checked against
# the release's checksum. The gate image and scripts/setup.sh run it; on a Mac or
# Windows, `brew install hugo` or `winget install Hugo.Hugo` does instead.
#
#   scripts/install-hugo.sh <folder>    puts the hugo binary in <folder>
set -euo pipefail

version=0.169.0
folder="${1:?install-hugo.sh: name the folder to install into}"

case "$(uname -m)" in
  x86_64) arch=amd64; sha256=2d2f0bfe357d8cb4cec02f6bb28a3bff34978c8dda44b618029c5eb9d66765a7 ;;
  aarch64 | arm64) arch=arm64; sha256=ddadaaccbf437e3d39c33140b80c2180c4f46d027d16b7716c1282ea2e14e00c ;;
  *) echo "install-hugo.sh: no Hugo pinned for $(uname -m)" >&2; exit 1 ;;
esac

file="hugo_${version}_linux-${arch}.tar.gz"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

curl --fail --silent --show-error --location --output "$work/$file" \
  "https://github.com/gohugoio/hugo/releases/download/v${version}/${file}"
echo "${sha256}  $work/$file" | sha256sum --check --quiet
mkdir -p "$folder"
tar -xzf "$work/$file" -C "$folder" hugo
