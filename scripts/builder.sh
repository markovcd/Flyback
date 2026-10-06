# Sourced by the scripts that build the Dockerfile, from the repo root. Sets
# `builder` and `cache` for `docker buildx build`.
#
# A self-hosted machine (and a developer's) builds on one named builder whose
# layer cache is capped (buildkitd.toml), so disk use is bounded and every build
# warms the next. GitHub's own runners keep nothing between jobs, so there the
# cache is GitHub's and the builder is the one setup-buildx-action made.
builder=()
cache=()

if [ "${RUNNER_ENVIRONMENT:-}" = github-hosted ]; then
  cache=(--cache-from type=gha)
else
  docker buildx inspect flyback >/dev/null 2>&1 \
    || docker buildx create --name flyback --driver docker-container --buildkitd-config scripts/buildkitd.toml >/dev/null
  builder=(--builder flyback)
fi
