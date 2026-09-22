#!/usr/bin/env bash
# Installs the latest Flyback release for this machine, or updates a copy it installed.
#
#   curl -fsSL https://raw.githubusercontent.com/markovcd/Flyback/main/install.sh | bash
#
# FLYBACK_VERSION=0.4.0 installs that release instead. FLYBACK_DIR is where the copy
# goes: a folder on Linux and Windows, the .app on macOS.

set -euo pipefail

repo=markovcd/Flyback

# The public half of the key releases are signed with; src/Flyback.App/Updates/release-key.pem.
release_key='-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAErHgbPx4egI1i8sCs5r+mRZBaGFB3
xktcInkfCBtFj5RIKR6nb326r7tyR6kjNSOJ/O8DnblXrd+mtnvG1/toug==
-----END PUBLIC KEY-----'

say() { printf '%s\n' "$*" >&2; }
die() { say "flyback: $*"; exit 1; }
need() { command -v "$1" >/dev/null 2>&1 || die "this needs $1, which is not installed"; }

need curl
need unzip
need openssl

case "$(uname -s)" in
  Linux) os=linux ;;
  Darwin) os=osx ;;
  MINGW* | MSYS* | CYGWIN*) os=win ;;
  *) die "no release is built for $(uname -s)" ;;
esac

case "$(uname -m)" in
  x86_64 | amd64) arch=x64 ;;
  arm64 | aarch64) arch=arm64 ;;
  *) die "no release is built for $(uname -m)" ;;
esac

# A shell under Rosetta reports x86_64 on Apple silicon.
if [ "$os" = osx ] && [ "$(sysctl -n hw.optional.arm64 2>/dev/null || echo 0)" = 1 ]; then
  arch=arm64
fi

rid=$os-$arch

case "$rid" in
  linux-x64 | osx-arm64 | win-x64) ;;
  *) die "releases are built for linux-x64, osx-arm64 and win-x64, not $rid; see https://github.com/$repo#build-and-publish to build it" ;;
esac

version=${FLYBACK_VERSION:-}

if [ -z "$version" ]; then
  latest=$(curl -fsSLI -o /dev/null -w '%{url_effective}' "https://github.com/$repo/releases/latest") \
    || die "could not reach GitHub"
  version=${latest##*/}
fi

version=${version#v}
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "'$version' is not a release version"

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

base=https://github.com/$repo/releases/download/v$version
package=flyback-$version-$rid.zip

say "Downloading Flyback $version for $rid"
curl -fL --progress-bar -o "$tmp/$package" "$base/$package" || die "v$version has no $package"
curl -fsSL -o "$tmp/SHA256SUMS" "$base/SHA256SUMS" || die "v$version has no SHA256SUMS"
curl -fsSL -o "$tmp/SHA256SUMS.sig" "$base/SHA256SUMS.sig" || die "v$version has no SHA256SUMS.sig"

printf '%s\n' "$release_key" > "$tmp/release-key.pem"
openssl dgst -sha256 -verify "$tmp/release-key.pem" -signature "$tmp/SHA256SUMS.sig" "$tmp/SHA256SUMS" >/dev/null 2>&1 \
  || die "SHA256SUMS is not signed with Flyback's release key"

expected=$(awk -v f="$package" '$2 == f || $2 == "*" f { print $1 }' "$tmp/SHA256SUMS")
actual=$(openssl dgst -sha256 -r "$tmp/$package" | cut -d' ' -f1)
[ -n "$expected" ] && [ "$expected" = "$actual" ] || die "$package does not match its checksum"

unzip -q "$tmp/$package" -d "$tmp/unpacked"

case "$os" in
  linux)
    payload=$tmp/unpacked/$rid
    plugins=plugins
    dest=${FLYBACK_DIR:-${XDG_DATA_HOME:-$HOME/.local/share}/flyback}
    ;;
  osx)
    payload=$tmp/unpacked/$rid/Flyback.app
    plugins=Contents/MacOS/plugins
    if [ -n "${FLYBACK_DIR:-}" ]; then
      dest=$FLYBACK_DIR
    elif [ -w /Applications ]; then
      dest=/Applications/Flyback.app
    else
      dest=$HOME/Applications/Flyback.app
    fi
    ;;
  win)
    payload=$tmp/unpacked/$rid
    plugins=plugins
    dest=${FLYBACK_DIR:-$(cygpath -u "$LOCALAPPDATA")/Programs/Flyback}
    ;;
esac

[ -d "$payload" ] || die "$package does not hold $rid"

# Files a running copy has open cannot be replaced on Windows, and elsewhere it would
# go on running the old version beside the new one.
if [ -d "$dest" ]; then
  if [ "$os" = win ]; then
    running=$(powershell.exe -NoProfile -Command \
      "(Get-Process | Where-Object { \$_.Path -like '$(cygpath -w "$dest")\\*' }).Count" | tr -d '\r')
    [ "${running:-0}" = 0 ] || die "Flyback is running from $dest; close it first"
  elif command -v pgrep >/dev/null 2>&1 && pgrep -f "$dest/" >/dev/null; then
    die "Flyback is running from $dest; close it first"
  fi
fi

# Replaced over what is there, so a plugin somebody added stays; a plugin the release
# ships is replaced whole, so none of its old assemblies load beside the new ones.
mkdir -p "$dest"
for plugin in "$payload/$plugins"/*/; do
  [ -d "$plugin" ] && rm -rf "${dest:?}/$plugins/$(basename "$plugin")"
done
cp -R "$payload/." "$dest/"

on_path() { case ":$PATH:" in *":$1:"*) return 0 ;; esac; return 1; }

case "$os" in
  linux | osx)
    bin=$HOME/.local/bin
    mkdir -p "$bin"

    if [ "$os" = linux ]; then
      programs=$dest
      ln -sf "$programs/Flyback" "$bin/flyback"

      applications=${XDG_DATA_HOME:-$HOME/.local/share}/applications
      mkdir -p "$applications"
      cat > "$applications/flyback.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Flyback
Comment=A patchable synthesizer for picture and sound
Exec="$programs/Flyback" %f
Terminal=false
Categories=AudioVideo;Audio;Graphics;
EOF
    else
      programs=$dest/Contents/MacOS
    fi

    ln -sf "$programs/flyback-cli" "$bin/flyback-cli"
    ln -sf "$programs/flyback-viewer" "$bin/flyback-viewer"

    on_path "$bin" || say "Add $bin to PATH for flyback-cli and flyback-viewer."
    ;;
  win)
    windows=$(cygpath -w "$dest")
    powershell.exe -NoProfile -Command "
      \$ErrorActionPreference = 'Stop'
      \$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Programs') + '\\Flyback.lnk')
      \$shortcut.TargetPath = '$windows\\Flyback.exe'
      \$shortcut.WorkingDirectory = '$windows'
      \$shortcut.Save()
      \$path = @([Environment]::GetEnvironmentVariable('Path', 'User') -split ';' | Where-Object { \$_ })
      if (\$path -notcontains '$windows') {
        [Environment]::SetEnvironmentVariable('Path', ((\$path + '$windows') -join ';'), 'User')
      }" >/dev/null || say "Could not add the Start menu shortcut or put $windows on PATH."
    ;;
esac

say "Flyback $version is installed in $dest."
