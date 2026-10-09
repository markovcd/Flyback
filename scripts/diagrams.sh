#!/usr/bin/env bash
# Draws every view in docs/diagrams/workspace.dsl into docs/diagrams/<view>.svg
# (ADR-0189). Needs only Docker: Structurizr checks the model and writes PlantUML,
# and PlantUML lays it out. Both images are pinned, so a redraw of an unchanged
# model is the same SVG.
#
#   ./scripts/diagrams.sh

set -euo pipefail

cd "$(dirname "$0")/.."

structurizr="structurizr/structurizr@sha256:721136283c2f9cf1ba69037bc9de136c579d66fdcb2d771cb60546ec68def1a5"
plantuml="plantuml/plantuml@sha256:d08610df482510844382caa4e016ba2bf7e3231f630f02ee12f250f3416c62b1"

# Git Bash would rewrite the containers' paths into Windows ones.
export MSYS_NO_PATHCONV=1

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
chmod 777 "$work"

docker run --rm -v "$PWD/docs/diagrams:/usr/local/structurizr:ro" -v "$work:/out" \
  "$structurizr" validate -w workspace.dsl
docker run --rm -v "$PWD/docs/diagrams:/usr/local/structurizr:ro" -v "$work:/out" \
  "$structurizr" export -w workspace.dsl -f plantuml/structurizr -o /out
rm -f "$work"/*-key.puml
docker run --rm -v "$work:/data" -w /data "$plantuml" -tsvg /data

for svg in "$work"/structurizr-*.svg; do
  view="$(basename "$svg" .svg | sed 's/^structurizr-//' | tr '[:upper:]' '[:lower:]')"
  cp "$svg" "docs/diagrams/$view.svg"
done
