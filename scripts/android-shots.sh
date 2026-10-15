#!/usr/bin/env bash
# Takes the Play listing's screenshots on the attached device or emulator, the APK already
# installed: each preset opened and playing, at a phone's 9:16 and a 7" and a 10" tablet's
# and a desktop's 16:9, sized with wm so every capture has the exact proportions Play asks for.
#
#   ./scripts/android-shots.sh <folder> [preset...]    Plasma, Nebula, Marble and Visualizer by default
#
# The folder gets <form>-<preset>-canvas.png and <form>-<preset>-picture.png, the second with
# the side button pressed, for phone, tablet7, tablet10 and desktop. The status bar is in
# demo mode, the microphone granted so no question covers a preset with a Line In, and the
# screen's size, density, rotation and status bar are put back on exit. Exit 0 when every capture was
# taken, 1 naming the first that was not, and 2 for a usage error.
set -euo pipefail

[ $# -ge 1 ] || { echo "usage: android-shots.sh <folder> [preset...]" >&2; exit 2; }
out="$1"
shift
presets=("$@")
[ ${#presets[@]} -gt 0 ] || presets=(Plasma Nebula Marble Visualizer)

package=app.flybackmodular.editor
mkdir -p "$out"

command -v adb >/dev/null || { echo "no adb on PATH" >&2; exit 2; }
[ "$(adb get-state 2>/dev/null || true)" = device ] || { echo "no device attached" >&2; exit 2; }

activity="$(adb shell cmd package resolve-activity --brief "$package" 2>/dev/null | tr -d '\r' | tail -n 1)"
[[ "$activity" == "$package"/* ]] || { echo "$package is not installed" >&2; exit 1; }

demo() { adb shell am broadcast -a com.android.systemui.demo -e command "$@" >/dev/null; }

cleanup() {
  demo exit 2>/dev/null || true
  adb shell wm size reset >/dev/null 2>&1 || true
  adb shell wm density reset >/dev/null 2>&1 || true
  adb shell settings put system user_rotation 0 >/dev/null 2>&1 || true
  adb shell settings put system accelerometer_rotation 1 >/dev/null 2>&1 || true
}
trap cleanup EXIT

adb shell settings put system accelerometer_rotation 0
adb shell pm grant "$package" android.permission.RECORD_AUDIO
adb shell settings put global sysui_demo_allowed 1
demo enter
demo clock -e hhmm 1200
demo battery -e level 100 -e plugged false
demo network -e wifi show -e level 4 -e mobile hide
demo notifications -e visible false

# The editor's answer to the broadcast; PLAY presses Play first, since a patch opens paused, and SIDE the side button.
ask() {
  local action="${1:-STATE}"
  [ $# -gt 0 ] && shift
  adb shell am broadcast -a "$package.$action" -p "$package" "$@" 2>/dev/null | tr -d '\r' | sed -n 's/.*data="\(.*\)"$/\1/p'
}

loaded() {
  local state
  state="$(ask)"
  [[ "$state" == *'"started":true'* && "$state" == *"\"preset\":\"$1\""* && "$state" != *'"soundRunsOn":null'* ]]
}

# One form factor: its name, its size as wm takes it (always portrait), its density and its rotation.
take() {
  local name="$1" size="$2" density="$3" rotation="$4" preset deadline
  adb shell wm size "$size"
  adb shell wm density "$density"
  adb shell settings put system user_rotation "$rotation"

  for preset in "${presets[@]}"; do
    adb shell am force-stop "$package"
    adb shell am start -W -n "$activity" --es preset "$preset" >/dev/null || { echo "FAIL: $preset would not start" >&2; exit 1; }

    deadline=$(( SECONDS + 120 ))
    until loaded "$preset"; do
      [ $SECONDS -lt $deadline ] || { echo "FAIL: $preset did not open on $name within two minutes" >&2; exit 1; }
      sleep 1
    done

    ask SIDE --ez shown false >/dev/null
    ask PLAY >/dev/null
    # Long enough for the picture to move off its first frame.
    sleep 8
    adb exec-out screencap -p > "$out/$name-${preset// /-}-canvas.png"
    ask SIDE >/dev/null
    sleep 3
    adb exec-out screencap -p > "$out/$name-${preset// /-}-picture.png"
    echo "$name: $preset" >&2
  done
}

take phone 1080x1920 420 0
take tablet7 1080x1920 240 1
take tablet10 1080x1920 200 1
take desktop 1080x1920 160 1

echo "PASS: $(ls "$out" | wc -l) captures in $out" >&2
