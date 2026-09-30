#!/usr/bin/env bash
# Builds the website as GitHub Pages serves it into the folder given: site/, with the
# web viewer at viewer/ and the web editor at editor/, both compiled ahead of time to
# WebAssembly (ADR-0160, ADR-0162), and the presets' stills at stills/ (ADR-0163).
# pages.yml runs it and uploads the folder; run here, it is what Pages would serve.
#
#   ./scripts/pages.sh <folder>
#
# Needs the wasm-tools workload and Python, which its Emscripten runs on. AOT=false
# skips the ahead-of-time compile, most of the build, for a quicker look. PRESETS_URL,
# where set, is where the preset site's own pages are linked from here.

set -euo pipefail

out="${1:?usage: ./scripts/pages.sh <folder>}"
aot="${AOT:-true}"

# Emptied first only where it is empty or an earlier build, never a folder somebody else filled.
if [[ -d "$out" && -n "$(ls -A "$out")" && ! -f "$out/.pages" ]]; then
  echo "pages.sh: $out has files it did not build; give it an empty or new folder." >&2
  exit 1
fi

build="$(mktemp -d)"
trap 'rm -rf "$build"' EXIT

rm -rf "$out"
mkdir -p "$out"
cp -r site/. "$out"
touch "$out/.pages"

# Only the plugins the desktop ships, as Release links; the preset site's stay on the preset site.
dotnet publish src/Flyback.Web -c Release -p:RunAOTCompilation="$aot" -p:CompressionEnabled=false -o "$build/web" -nologo -v:q
dotnet publish src/Flyback.WebEditor -c Release -p:RunAOTCompilation="$aot" -p:CompressionEnabled=false -o "$build/editor" -nologo -v:q
cp -r "$build/web/wwwroot/viewer" "$out/viewer"
cp -r "$build/editor/wwwroot" "$out/editor"
./scripts/stills.sh "$out/stills"

# What only the preset site can do is left out: a link marked data-preset-site goes, and
# the toolbar's Plugins leads to the plugin guide, since the shared plugins are listed on
# the preset site alone.
find "$out" -name '*.html' -exec sed -i -E \
  -e 's#<a [^>]*data-preset-site[^>]*>[^<]*</a>##g' \
  -e 's#href="(\.\./)?shared-plugins\.html">Plugins</a>#href="\1plugins.html">Plugins</a>#g' {} +
sed -i 's#href="plugins.html">Plugins</a>#href="plugins.html" aria-current="page">Plugins</a>#' "$out/plugins.html"

# The site links the preset site's pages as neighbors, which they are on the preset site
# itself. Here they are elsewhere, at PRESETS_URL.
if [[ -n "${PRESETS_URL:-}" ]]; then
  base="${PRESETS_URL%/}"
  for page in src/Flyback.Server/wwwroot/*.html; do
    name="$(basename "$page")"
    find "$out" -name '*.html' -exec sed -i -E "s#href=\"(\.\./)?$name#href=\"$base/$name#g" {} +
  done
fi
