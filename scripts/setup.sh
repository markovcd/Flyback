#!/usr/bin/env bash
# Sets up a machine that has no .NET SDK to build and test Flyback: a cloud session's
# container, most often.
#
#   ./scripts/setup.sh            the SDK global.json names, into ~/.dotnet, and the wasm-tools workload
#   ./scripts/setup.sh --android  the android workload as well, for compiling Flyback.Plugins.AndroidIO
#
# The usual download hosts (builds.dotnet.microsoft.com, dotnetcli.azureedge.net) are
# blocked behind a cloud session's proxy and packages.microsoft.com is not, so the SDK
# comes as its Debian packages, extracted into ~/.dotnet rather than installed: no root,
# and no dependency check to fail on a distribution's libicu. The workloads come from
# NuGet. The page scenarios get the Playwright Chromium under /opt/pw-browsers through
# FLYBACK_CHROMIUM, so they run rather than skip. What a shell then needs is written to
# ~/.flyback-env and sourced from ~/.bashrc; the shell that ran this sources it by hand.
set -euo pipefail

cd "$(dirname "$0")/.."

feed=https://packages.microsoft.com/debian/12/prod/dists/bookworm/main
root="$HOME/.dotnet"
env_file="$HOME/.flyback-env"
android=false

for arg in "$@"; do
  case "$arg" in
    --android) android=true ;;
    *) echo "unknown argument: $arg" >&2; exit 2 ;;
  esac
done

version="$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' global.json | head -1)"
band="${version%.*}"

case "$(uname -m)" in
  x86_64) arch=amd64 ;;
  aarch64) arch=arm64 ;;
  *) echo "no .NET packages for $(uname -m)" >&2; exit 1 ;;
esac

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# An SDK this script put under ~/.dotnet comes before any the machine has.
[ -x "$root/dotnet" ] && export DOTNET_ROOT="$root" PATH="$root:$PATH"

# The newest version of a package in the feed, or the one whose version starts with $2.
newest() {
  awk -v want="$1" -v prefix="${2:-}" 'BEGIN { RS = ""; FS = "\n" }
    {
      p = ""; v = ""; f = ""
      for (i = 1; i <= NF; i++) {
        if ($i ~ /^Package: /) p = substr($i, 10)
        else if ($i ~ /^Version: /) v = substr($i, 10)
        else if ($i ~ /^Filename: /) f = substr($i, 11)
      }
      if (p == want && (prefix == "" || index(v, prefix) == 1)) print v, f
    }' "$packages" | sort -V | tail -1 | cut -d' ' -f2
}

# dotnet --version fails under a global.json the installed SDKs do not satisfy.
if dotnet --version >/dev/null 2>&1; then
  echo "SDK $(dotnet --version) satisfies global.json"
else
  work="$(mktemp -d)"
  packages="$work/Packages"
  echo "fetching the SDK $version packages from packages.microsoft.com"
  curl -fsSL "$feed/binary-$arch/Packages.gz" | gunzip > "$packages"

  files=("$(newest "dotnet-sdk-$band" "$version-")")
  [ -n "${files[0]}" ] || { echo "no dotnet-sdk-$band $version in the feed" >&2; exit 1; }

  for name in dotnet-host "dotnet-hostfxr-$band" "dotnet-runtime-$band" "aspnetcore-runtime-$band" \
              "dotnet-targeting-pack-$band" "aspnetcore-targeting-pack-$band" "dotnet-apphost-pack-$band" \
              netstandard-targeting-pack-2.1; do
    file="$(newest "$name")"
    [ -n "$file" ] || { echo "no $name in the feed" >&2; exit 1; }
    files+=("$file")
  done

  mkdir -p "$work/tree"
  for file in "${files[@]}"; do
    echo "  ${file##*/}"
    curl -fsSL "https://packages.microsoft.com/debian/12/prod/$file" -o "$work/package.deb"
    dpkg-deb -x "$work/package.deb" "$work/tree"
  done

  mkdir -p "$root"
  cp -a "$work/tree/usr/share/dotnet/." "$root/"
  rm -rf "$work"
  export DOTNET_ROOT="$root" PATH="$root:$PATH"
  echo "SDK $(dotnet --version) in $root"
fi

installed="$(dotnet workload list 2>/dev/null)"
wanted=(wasm-tools)
$android && wanted+=(android)
for workload in "${wanted[@]}"; do
  if grep -q "^$workload " <<< "$installed"; then
    echo "workload $workload is installed"
  else
    echo "installing the $workload workload"
    dotnet workload install "$workload" --skip-sign-check
  fi
done

chromium="${FLYBACK_CHROMIUM:-}"
if [ -z "$chromium" ]; then
  for candidate in /opt/pw-browsers/chromium-*/chrome-linux/chrome; do
    [ -x "$candidate" ] && chromium="$candidate"
  done
fi

{
  echo '# Written by scripts/setup.sh: the .NET SDK under ~/.dotnet and the browser the page scenarios open.'
  echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
  if [ -x "$root/dotnet" ]; then
    echo "export DOTNET_ROOT=\"\$HOME/.dotnet\""
    echo 'case ":$PATH:" in *":$HOME/.dotnet:"*) ;; *) export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH" ;; esac'
  fi
  [ -n "$chromium" ] && echo "export FLYBACK_CHROMIUM=\"$chromium\""
} > "$env_file"

if [ -f "$HOME/.bashrc" ] && ! grep -q 'flyback-env' "$HOME/.bashrc"; then
  printf '\n[ -f "$HOME/.flyback-env" ] && . "$HOME/.flyback-env"\n' >> "$HOME/.bashrc"
fi

echo
[ -n "$chromium" ] && echo "the page scenarios open $chromium" || echo "no Playwright Chromium under /opt/pw-browsers; the page scenarios skip unless one is on PATH"
echo "source $env_file"
