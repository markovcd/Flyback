#!/usr/bin/env bash
# Runs the Android editor on a device or an emulator and asks it how it is doing, so
# a session checks the APK without a window (ADR-0184): the app boots on a preset, the
# picture draws, AudioTrack plays, the sound's IL runs under Mono's JIT, the state
# broadcast answers, and all of it holds after a rotation.
#
#   ./scripts/android-smoke.sh                   Sidebands on the attached device, or on the AVD named flyback
#   ./scripts/android-smoke.sh Visualizer        another preset
#   ./scripts/android-smoke.sh --interpreted     the sound on the interpreter; its speed is then not timed
#   ./scripts/android-smoke.sh --avd phone       the AVD to boot when no device is attached
#   ./scripts/android-smoke.sh --no-build        the APK already installed
#
# With no device attached it boots the AVD headless, as the handoff says to
# (-no-window -gpu swiftshader_indirect -audio none), and shuts down only the emulator
# it started; a device already attached is used and left running. The build installs
# for the device's ABI, so it needs DOTNET_ROOT, JAVA_HOME and ANDROID_HOME as the
# handoff describes. Each answer the editor gives is printed as it comes; the last one
# is the whole JSON on stdout. Exit 0 when every check holds, 1 naming the first that
# did not, with the app's logcat after it, and 2 for a usage error.
# With SMOKE_SHOTS naming a folder, a failure also leaves the screen, the top
# activity and the whole logcat there.
set -euo pipefail

cd "$(dirname "$0")/.."

package=app.flybackmodular.editor
preset=Sidebands
avd=flyback
interpreted=false
build=true

while [ $# -gt 0 ]; do
  case "$1" in
    --interpreted) interpreted=true ;;
    --no-build) build=false ;;
    --avd) shift; [ $# -gt 0 ] || { echo "--avd needs a name" >&2; exit 2; }; avd="$1" ;;
    --*) echo "unknown argument: $1" >&2; exit 2 ;;
    *) preset="$1" ;;
  esac
  shift
done

sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$HOME/Android/Sdk}}"
export PATH="$sdk/platform-tools:$sdk/emulator:$PATH"

command -v adb >/dev/null || { echo "no adb: set ANDROID_HOME to an SDK with platform-tools" >&2; exit 2; }

emulator_pid=""
rotated=false

cleanup() {
  if $rotated; then
    adb shell settings put system user_rotation 0 >/dev/null 2>&1 || true
    adb shell settings put system accelerometer_rotation 1 >/dev/null 2>&1 || true
  fi
  if [ -n "$emulator_pid" ]; then
    adb emu kill >/dev/null 2>&1 || true
    kill "$emulator_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

fail() {
  echo "FAIL: $*" >&2
  if [ -n "${SMOKE_SHOTS:-}" ]; then
    mkdir -p "$SMOKE_SHOTS"
    adb exec-out screencap -p > "$SMOKE_SHOTS/screen.png" 2>/dev/null || true
    adb shell dumpsys activity top 2>/dev/null | head -n 200 > "$SMOKE_SHOTS/activity.txt" || true
    adb logcat -d 2>/dev/null > "$SMOKE_SHOTS/logcat.txt" || true
    echo "--- the screen, the top activity and the whole logcat are in $SMOKE_SHOTS" >&2
  fi
  echo "--- the device's audio output threads and tracks" >&2
  adb shell dumpsys media.audio_flinger 2>/dev/null | grep -E -A3 "Output thread|Standby|Tracks|Frames written|underrun|Hal stream" | head -n 60 >&2 || true
  if pid="$(adb shell pidof "$package" 2>/dev/null | tr -d '[:space:]')" && [ -n "$pid" ]; then
    echo "--- logcat of $package ($pid)" >&2
    adb logcat -d --pid="$pid" 2>/dev/null | tail -n 80 >&2 || true
  else
    echo "--- the app is not running; the crash buffer:" >&2
    adb logcat -d -b crash 2>/dev/null | tail -n 80 >&2 || true
  fi
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

booted() { [ "$(adb shell getprop sys.boot_completed 2>/dev/null | tr -d '[:space:]')" = 1 ]; }

if [ "$(adb get-state 2>/dev/null || true)" != device ]; then
  command -v emulator >/dev/null || { echo "no device attached and no emulator under $sdk" >&2; exit 2; }
  echo "no device attached; booting the AVD '$avd'" >&2
  emulator -avd "$avd" -no-window -gpu swiftshader_indirect -audio none -no-boot-anim -no-snapshot >/dev/null 2>&1 &
  emulator_pid=$!
  await 240 booted || fail "the AVD '$avd' did not boot within four minutes"
else
  await 120 booted || fail "the attached device did not finish booting"
fi

echo "device: $(adb shell getprop ro.product.model | tr -d '\r') on API $(adb shell getprop ro.build.version.sdk | tr -d '\r'), $(adb shell getprop ro.product.cpu.abi | tr -d '\r')" >&2

if $build; then
  # Never -p:RuntimeIdentifier: it flows into every referenced project and rewrites their lock files.
  dotnet build src/Flyback.Editor.Android -c Release -v minimal --nologo -t:Install -p:AndroidSdkDirectory="$sdk" >&2
fi

activity="$(adb shell cmd package resolve-activity --brief "$package" 2>/dev/null | tr -d '\r' | tail -n 1)"
[[ "$activity" == "$package"/* ]] || fail "$package is not installed"

adb shell am force-stop "$package"
adb shell am start -W -n "$activity" --es preset "$preset" --ez interpreted "$interpreted" >/dev/null \
  || fail "the activity would not start"

# The editor's answer to the broadcast: the JSON in the result's data, or nothing before it listens.
ask() {
  adb shell am broadcast -a "$package.STATE" -p "$package" 2>/dev/null | tr -d '\r' | sed -n 's/.*data="\(.*\)"$/\1/p'
}

# A field of the last answer: a string without its quotes, or a bare number, true, false or null.
field() {
  printf '%s' "$state" | sed -n "s/.*\"$1\":\"\([^\"]*\)\".*/\1/p; t; s/.*\"$1\":\([^,}]*\).*/\1/p"
}

state=""
asked_at=$SECONDS

# Done when the sound has been timed, or, on the interpreter, once its program is in.
settled() {
  state="$(ask)"
  [ -n "$state" ] || return 1
  echo "$(( SECONDS - asked_at ))s: $state" >&2
  if $interpreted; then
    [ "$(field soundRunsOn)" = interpreter ]
  else
    awk -v speed="$(field soundSpeed)" 'BEGIN { exit !(speed > 0) }'
  fi
}

await 120 settled || fail "the sound was not timed within two minutes; the last answer: ${state:-none}"

check() {
  [ "$(field "$1")" = "$2" ] || fail "$1 is '$(field "$1")', not '$2'"
}

above() {
  awk -v value="$(field "$1")" -v least="$2" 'BEGIN { exit !(value > least) }' || fail "$1 is '$(field "$1")', not above $2"
}

check started true
check preset "$preset"
check soundBackend AudioTrack
above framesPerSecond 0
if $interpreted; then
  check soundRunsOn interpreter
  check soundSpeed 0
else
  check soundRunsOn IL
  above soundSpeed 1
fi

# Sideways, then ask again: the activity is kept through the rotation, so the same editor answers.
rotated=true
adb shell settings put system accelerometer_rotation 0
adb shell settings put system user_rotation 1
sleep 3

state="$(ask)"
[ -n "$state" ] || fail "the editor did not answer after a rotation"
echo "rotated: $state" >&2
check started true
check preset "$preset"
above framesPerSecond 0

echo "$state"
echo "PASS: $preset plays on $(field soundBackend), the sound on $(field soundRunsOn) at $(field soundSpeed)x, the picture at $(field framesPerSecond) fps, upright and sideways" >&2
