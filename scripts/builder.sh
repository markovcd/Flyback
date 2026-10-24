# Sourced by the scripts that build the Dockerfile, from the repo root. Sets
# `builder` and `cache` for `docker buildx build`.
#
# A developer's machine builds on one named builder whose
# layer cache is capped (buildkitd.toml), so disk use is bounded and every build
# warms the next. GitHub's own runners keep nothing between jobs, so there the
# builder is the one setup-buildx-action made, and it reads the base and tools
# layers from the site's scope in GitHub's cache. Nothing on them writes the
# gate's own layers: exporting them takes longer than every layer they would save.
#
# The builder's container is capped in memory (FLYBACK_BUILDER_MEMORY, default
# 12g, swap included), so a heavy build is killed inside it rather than
# starving the host.
builder=()
cache=()
limit="${FLYBACK_BUILDER_MEMORY:-12g}"

if [ "${RUNNER_ENVIRONMENT:-}" = github-hosted ]; then
  cache=(--cache-from type=gha,scope=site)
else
  docker buildx inspect flyback >/dev/null 2>&1 \
    || docker buildx create --name flyback --driver docker-container --buildkitd-config scripts/buildkitd.toml \
         --driver-opt "memory=$limit,memory-swap=$limit" >/dev/null
  docker update --memory "$limit" --memory-swap "$limit" buildx_buildkit_flyback0 >/dev/null 2>&1 || true
  builder=(--builder flyback)
fi
