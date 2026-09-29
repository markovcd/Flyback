#!/usr/bin/env bash
# Draws every preset's still, with the index the galleries read them by, into the
# folder given (ADR-0163). The editor and the command line are published side by side
# first, as a release lays them out, so the plugins the build ships load.
#
#   ./stills.sh <folder>
#
# CONFIGURATION picks the plugins: Release is what the desktop ships, and "All plugins"
# adds the preset site's, which the web viewer carries. Version, where it is set, is
# the build the index says drew it, and has to be the one the programs reading it were
# built with.

set -euo pipefail

out="$1"
configuration="${CONFIGURATION:-Release}"
draw="$(mktemp -d)"
trap 'rm -rf "$draw"' EXIT

dotnet publish src/Flyback.App -c "$configuration" -o "$draw" -nologo -v:q
dotnet publish src/Flyback.Cli -c "$configuration" -o "$draw" -nologo -v:q
dotnet "$draw/flyback-cli.dll" stills --out "$out"
