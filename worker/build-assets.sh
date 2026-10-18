#!/usr/bin/env bash
# Assembles worker/public, everything the Worker serves as a file: the pages of site/,
# the web viewer at /viewer/ (ADR-0160), the web editor at /editor/ (ADR-0162) and every
# preset's still at /stills/ (ADR-0163). A framework file past the 25 MiB a static asset
# may be goes to worker/large instead, for the Worker workflow to put in R2, where the
# Worker serves it from on the same path.
#
#   worker/build-assets.sh [--no-aot] [--no-editor] [--no-stills] [--built <folder>]
#
# The viewer and editor are compiled ahead of time unless --no-aot, which needs the
# wasm-tools workload, as the editor does in any case. The --no- flags are for trying
# the pages on a machine without it; a deploy takes none of them. --built takes the
# viewer, the editor and the stills from a folder holding viewer/, editor/ and stills/
# as the Dockerfile's site stages publish them, rather than building them here.

set -euo pipefail

aot=true
editor=true
stills=true
built=""

while [ $# -gt 0 ]; do
  case "$1" in
    --no-aot) aot=false ;;
    --no-editor) editor=false ;;
    --no-stills) stills=false ;;
    --built) [ $# -ge 2 ] || { echo "build-assets.sh: --built names a folder" >&2; exit 2; }; built="$2"; shift ;;
    *) echo "build-assets.sh: $1: not a flag this takes" >&2; exit 2 ;;
  esac
  shift
done

root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/worker/public"
large="$root/worker/large"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

rm -rf "$out" "$large"
mkdir -p "$out"

cp -r "$root/site/." "$out/"

if [ -n "$built" ]; then
  cp -r "$built/." "$work/"
else
  dotnet publish "$root/src/Flyback.Viewer.Web" -c Release -p:RunAOTCompilation=$aot -o "$work/viewer" -nologo -v:q
  if [ "$editor" = true ]; then
    dotnet publish "$root/src/Flyback.Editor.Web" -c Release -p:RunAOTCompilation=$aot -o "$work/editor" -nologo -v:q
  fi
  if [ "$stills" = true ]; then
    (cd "$root" && ./scripts/stills.sh "$work/stills")
  fi
fi

cp -r "$work/viewer/wwwroot/." "$out/"

if [ "$editor" = true ]; then
  mkdir -p "$out/editor"
  cp -r "$work/editor/wwwroot/." "$out/editor/"
fi

if [ "$stills" = true ]; then
  cp -r "$work/stills" "$out/stills"
fi

# What a browser that came over HTTPS is told to keep to, on the files the Worker never sees; it says the same on its own answers.
printf '/*\n  Strict-Transport-Security: max-age=2592000\n' > "$out/_headers"

# Cloudflare compresses at the edge, so the copies the publishes wrote would only ever be served as files.
find "$out" -type f \( -name '*.br' -o -name '*.gz' \) -delete

# Workers serves a static asset of 25 MiB at most. A fingerprinted framework file past
# it is served from R2; anything else that large has nowhere to go.
find "$out" -type f -size +25M | while read -r file; do
  path="${file#"$out"/}"
  if ! [[ "$path" =~ ^(viewer|editor)/_framework/[A-Za-z0-9._-]+\.[a-z0-9]{10}\.[a-z]+$ ]]; then
    echo "build-assets.sh: $path is $(( $(stat -c %s "$file") >> 20 )) MiB, past the 25 MiB a Workers asset may be." >&2
    exit 1
  fi
  echo "build-assets.sh: $path is $(( $(stat -c %s "$file") >> 20 )) MiB, so it is served from R2."
  mkdir -p "$large/$(dirname "$path")"
  mv "$file" "$large/$path"
done

echo "$(find "$out" -type f | wc -l) files, $(du -sh "$out" | cut -f1), in $out"
