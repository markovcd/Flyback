#!/usr/bin/env bash
# Runs the iOS editor in the simulator and asks it how it is doing, so a session
# checks the app without a window: the app boots on a preset, Play is pressed, the
# picture draws, CoreAudio plays, and the state notification answers.
#
#   ./scripts/ios-smoke.sh                   Sidebands on a booted simulator, or on the first iPad there is
#   ./scripts/ios-smoke.sh Visualizer        another preset
#   ./scripts/ios-smoke.sh --interpreted     the sound on the engine's interpreter, as on a device
#   ./scripts/ios-smoke.sh --no-build        the app already built
#
# Needs a Mac with Xcode and the ios workload. A simulator already booted is used and
# left running; one this script boots is shut down after. Each answer is printed as it
# comes; the last one is the whole JSON on stdout. Exit 0 when every check holds, 1
# naming the first that did not, with the app's output after it, and 2 for a usage error.
# With SMOKE_SHOTS naming a folder, the screen is left there, and on a failure the app's output too.
set -euo pipefail

cd "$(dirname "$0")/.."

bundle=app.flybackmodular.editor
preset=Sidebands
interpreted=false
build=true

while [ $# -gt 0 ]; do
  case "$1" in
    --interpreted) interpreted=true ;;
    --no-build) build=false ;;
    --*) echo "unknown argument: $1" >&2; exit 2 ;;
    *) preset="$1" ;;
  esac
  shift
done

command -v xcrun >/dev/null || { echo "no xcrun: this needs a Mac with Xcode" >&2; exit 2; }

app=src/Flyback.Editor.iOS/bin/Release/net10.0-ios/iossimulator-arm64/Flyback.Editor.iOS.app
logs="$(mktemp -d)"
booted_here=""

cleanup() {
  [ -n "$booted_here" ] && xcrun simctl shutdown "$booted_here" >/dev/null 2>&1 || true
}
trap cleanup EXIT

shot() {
  [ -n "${SMOKE_SHOTS:-}" ] || return 0
  mkdir -p "$SMOKE_SHOTS"
  xcrun simctl io "$device" screenshot "$SMOKE_SHOTS/$1.png" >/dev/null 2>&1 || true
}

fail() {
  echo "FAIL: $*" >&2
  shot failed
  if [ -n "${SMOKE_SHOTS:-}" ]; then cp "$logs"/* "$SMOKE_SHOTS"/ 2>/dev/null || true; fi
  echo "--- the app's output" >&2
  tail -n 80 "$logs/stdout.txt" "$logs/stderr.txt" >&2 2>/dev/null || true
  exit 1
}

# Waits up to $1 seconds for the command in the rest of the arguments to succeed.
await() {
  local deadline=$(( SECONDS + $1 ))
  shift
  until "$@"; do
    [ $SECONDS -lt $deadline ] || return 1
    sleep 1
  done
}

# The first booted simulator, else the first iPad, as its UDID.
pick() {
  xcrun simctl list devices available -j | python3 -c '
import json, sys
devices = [d for runtime, ds in json.load(sys.stdin)["devices"].items() if "iOS" in runtime for d in ds]
booted = [d for d in devices if d["state"] == "Booted"]
ipads = [d for d in devices if d["name"].startswith("iPad")]
chosen = (booted or ipads or devices or [None])[0]
print(chosen["udid"] + " " + chosen["state"] if chosen else "")'
}

read -r device state <<< "$(pick)" || true
[ -n "${device:-}" ] || { echo "no iOS simulator: add one in Xcode's Devices and Simulators" >&2; exit 2; }

if [ "$state" != Booted ]; then
  echo "booting the simulator $device" >&2
  xcrun simctl boot "$device"
  booted_here="$device"
fi
xcrun simctl bootstatus "$device" >/dev/null || fail "the simulator did not boot"

if $build; then
  dotnet build src/Flyback.Editor.iOS -c Release -v minimal --nologo >&2
fi

[ -d "$app" ] || fail "no app at $app"
xcrun simctl install "$device" "$app" || fail "the app would not install"

xcrun simctl terminate "$device" "$bundle" >/dev/null 2>&1 || true
container="$(xcrun simctl get_app_container "$device" "$bundle" data)"
readout="$container/Library/Application Support/readout.json"
rm -f "$readout"

args=(--preset "$preset")
$interpreted && args+=(--interpreted)
xcrun simctl launch --stdout="$logs/stdout.txt" --stderr="$logs/stderr.txt" "$device" "$bundle" "${args[@]}" >/dev/null \
  || fail "the app would not launch"

state=""

# The editor's answer to a notification: it writes the readout, which this waits for.
ask() {
  rm -f "$readout"
  xcrun simctl spawn "$device" notifyutil -p "$bundle.${1:-STATE}" >/dev/null 2>&1 || return 1
  await 5 test -f "$readout" || return 1
  state="$(cat "$readout")"
}

# A field of the last answer: a string without its quotes, or a bare number, true, false or null.
field() {
  printf '%s' "$state" | sed -n "s/.*\"$1\":\"\([^\"]*\)\".*/\1/p; t; s/.*\"$1\":\([^,}]*\).*/\1/p"
}

asked_at=$SECONDS

loaded() { ask && [ "$(field soundRunsOn)" != null ] && [ -n "$(field soundRunsOn)" ]; }

await 120 loaded || fail "the patch did not open within two minutes; the last answer: ${state:-none}"
ask PLAY || fail "the editor did not answer PLAY"
echo "$(( SECONDS - asked_at ))s, Play pressed: $state" >&2

settled() {
  ask || return 1
  echo "$(( SECONDS - asked_at ))s: $state" >&2
  awk -v fps="$(field framesPerSecond)" 'BEGIN { exit !(fps > 0) }'
}

await 60 settled || fail "the picture did not draw within a minute; the last answer: ${state:-none}"
shot playing

check() {
  [ "$(field "$1")" = "$2" ] || fail "$1 is '$(field "$1")', not '$2'"
}

check started true
check preset "$preset"
check soundBackend CoreAudio
$interpreted && check soundRunsOn interpreter

echo "$state"
echo "PASS: $preset plays on $(field soundBackend), the sound on $(field soundRunsOn) at $(field soundSpeed)x, the picture on $(field backend) ($(field renderer)) at $(field framesPerSecond) fps" >&2
