#!/usr/bin/env bash
# Installs the latest Flyback release for this machine, or updates a copy it installed.
#
#   curl -fsSL https://raw.githubusercontent.com/markovcd/Flyback/main/install.sh | bash
#   curl -fsSL https://raw.githubusercontent.com/markovcd/Flyback/main/install.sh | bash -s -- --uninstall
#
# FLYBACK_VERSION=0.4.0 installs that release instead. FLYBACK_DIR is where the copy
# goes: a folder on Linux and Windows, the .app on macOS. --uninstall removes the copy
# and everything this script put beside it.

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

uninstall=false

for argument in "$@"; do
  case "$argument" in
    --uninstall) uninstall=true ;;
    -h | --help)
      say "usage: install.sh [--uninstall]"
      say "  FLYBACK_VERSION  the release to install, rather than the latest"
      say "  FLYBACK_DIR      where the copy goes (the .app on macOS)"
      exit 0
      ;;
    *) die "unknown option $argument" ;;
  esac
done

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
bin=$HOME/.local/bin
applications=${XDG_DATA_HOME:-$HOME/.local/share}/applications

case "$os" in
  linux)
    plugins=plugins
    dest=${FLYBACK_DIR:-${XDG_DATA_HOME:-$HOME/.local/share}/flyback}
    programs=$dest
    shell=Flyback
    ;;
  osx)
    plugins=Contents/MacOS/plugins
    if [ -n "${FLYBACK_DIR:-}" ]; then
      dest=$FLYBACK_DIR
    elif [ -w /Applications ] || [ -d /Applications/Flyback.app ]; then
      dest=/Applications/Flyback.app
    else
      dest=$HOME/Applications/Flyback.app
    fi
    programs=$dest/Contents/MacOS
    shell=Flyback
    ;;
  win)
    plugins=plugins
    dest=${FLYBACK_DIR:-$(cygpath -u "$LOCALAPPDATA")/Programs/Flyback}
    programs=$dest
    shell=Flyback.exe
    windows=$(cygpath -w "$dest")
    ;;
esac

commands=(flyback-cli flyback-viewer)
[ "$os" = linux ] && commands+=(flyback)

# What a command in $bin links to.
target() { case "$1" in flyback) echo "$programs/Flyback" ;; *) echo "$programs/$1" ;; esac; }

# Files a running copy has open cannot be replaced or removed on Windows, and elsewhere
# it would go on running the old version beside the new one.
refuse_if_running() {
  [ -d "$dest" ] || return 0

  if [ "$os" = win ]; then
    running=$(powershell.exe -NoProfile -Command \
      "(Get-Process | Where-Object { \$_.Path -like '$windows\\*' }).Count" | tr -d '\r')
    [ "${running:-0}" = 0 ] || die "Flyback is running from $dest; close it first"
  elif [ "$os" = linux ]; then
    # By executable, since a command started through its link in $bin names the link.
    for exe in /proc/[0-9]*/exe; do
      case "$(readlink "$exe" 2>/dev/null)" in
        "$programs"/*) die "Flyback is running from $dest; close it first" ;;
      esac
    done
  elif pgrep -f "$programs/" >/dev/null; then
    die "Flyback is running from $dest; close it first"
  fi
}

if $uninstall; then
  refuse_if_running

  removed=false

  # Only a folder holding Flyback is removed, so a mistyped FLYBACK_DIR takes nothing with it.
  if [ -d "$dest" ]; then
    [ -f "$programs/$shell" ] || die "$dest does not hold Flyback, so it is left alone"
    rm -rf "${dest:?}"
    say "Removed $dest"
    removed=true
  fi

  # A link, desktop entry or shortcut is only this script's if it points into this copy.
  if [ "$os" = win ]; then
    report=$(powershell.exe -NoProfile -Command "
      \$ErrorActionPreference = 'Stop'
      \$link = [Environment]::GetFolderPath('Programs') + '\\Flyback.lnk'
      if ((Test-Path \$link) -and (New-Object -ComObject WScript.Shell).CreateShortcut(\$link).TargetPath -eq '$windows\\Flyback.exe') {
        Remove-Item \$link
        'Removed ' + \$link
      }
      \$path = @([Environment]::GetEnvironmentVariable('Path', 'User') -split ';' | Where-Object { \$_ })
      if (\$path -contains '$windows') {
        [Environment]::SetEnvironmentVariable('Path', (@(\$path | Where-Object { \$_ -ne '$windows' }) -join ';'), 'User')
        'Removed $windows from PATH'
      }" | tr -d '\r') || die "could not remove the Start menu shortcut or the PATH entry"

    if [ -n "$report" ]; then
      say "$report"
      removed=true
    fi
  else
    for command in "${commands[@]}"; do
      link=$bin/$command
      if [ -L "$link" ] && [ "$(readlink "$link")" = "$(target "$command")" ]; then
        rm -f "$link"
        say "Removed $link"
        removed=true
      fi
    done

    entry=$applications/flyback.desktop
    if [ "$os" = linux ] && [ -f "$entry" ] && grep -qF "Exec=\"$programs/Flyback\"" "$entry"; then
      rm -f "$entry"
      say "Removed $entry"
      removed=true
    fi
  fi

  $removed || say "Flyback is not installed in $dest."
  exit 0
fi

need curl
need unzip
need openssl

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

payload=$tmp/unpacked/$rid
[ "$os" = osx ] && payload=$payload/Flyback.app
[ -d "$payload" ] || die "$package does not hold $rid"

refuse_if_running

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
    mkdir -p "$bin"
    for command in "${commands[@]}"; do ln -sf "$(target "$command")" "$bin/$command"; done

    if [ "$os" = linux ]; then
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
    fi

    on_path "$bin" || say "Add $bin to PATH for flyback-cli and flyback-viewer."
    ;;
  win)
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
