# Sourced, not run: sets version, tag and version_code for a release, or exits.
#
#   . ./scripts/release-version.sh [version]
#
# version is X.Y.Z. Blank bumps the minor version of the latest vX.Y.Z tag, or starts
# at 0.1.0 with none. version_code is the Android one, rising with every release.

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

IFS=. read -r major minor patch <<< "$version"
if [ "$major" -gt 2000 ] || [ "$minor" -gt 999 ] || [ "$patch" -gt 999 ]; then
  echo "release: $version has no Android version code; minor and patch stop at 999" >&2
  exit 1
fi
version_code=$((10#$major * 1000000 + 10#$minor * 1000 + 10#$patch))
