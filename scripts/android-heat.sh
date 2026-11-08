#!/usr/bin/env bash
# Measures how warm the Android editor makes a phone at each count of sound cores
# (ADR-0230): plays a preset for a short run per count, logging once a second the skin,
# CPU and battery temperatures, the throttling status, the power drawn, each CPU
# cluster's clock and the editor's soundSpeed and soundCores.
#
#   ./scripts/android-heat.sh                      Mycelium on 1, 2 and 4 cores, 20 s each
#   ./scripts/android-heat.sh Acid --cores "1 4"   another preset and other counts
#   ./scripts/android-heat.sh --seconds 15         shorter runs; 20 s is the most
#   ./scripts/android-heat.sh --no-build           the APK already installed
#   ./scripts/android-heat.sh --out heat.csv       where the log goes (default: android-heat.csv)
#
# Kind to the phone: each run waits for the skin to cool to --cool (33 C) first, gives up
# after ten minutes of waiting, and stops the app and the whole script the moment the
# skin passes --hot (38 C). The app is force-stopped after every run and on any exit.
# Unplugged is best: charging heats the battery and blurs the power reading, so use
# wireless debugging. Prints one line per run, with the mean power, which is what turns
# into heat. Exit 0 when every run finished, 1 when one was stopped, 2 for a usage error.
set -euo pipefail

cd "$(dirname "$0")/.."

package=app.flybackmodular.editor
preset=Mycelium
counts="1 2 4"
seconds=20
cool=33
hot=38
build=true
out=android-heat.csv

while [ $# -gt 0 ]; do
  case "$1" in
    --cores) shift; counts="${1:?--cores needs counts}" ;;
    --seconds) shift; seconds="${1:?--seconds needs a number}" ;;
    --cool) shift; cool="${1:?--cool needs degrees}" ;;
    --hot) shift; hot="${1:?--hot needs degrees}" ;;
    --out) shift; out="${1:?--out needs a file}" ;;
    --no-build) build=false ;;
    --*) echo "unknown argument: $1" >&2; exit 2 ;;
    *) preset="$1" ;;
  esac
  shift
done

[ "$seconds" -ge 1 ] && [ "$seconds" -le 20 ] || { echo "--seconds is 1 to 20: a phone gets hot fast" >&2; exit 2; }

sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$HOME/Android/Sdk}}"
export PATH="$sdk/platform-tools:$PATH"
command -v adb >/dev/null || { echo "no adb: set ANDROID_HOME to an SDK with platform-tools" >&2; exit 2; }
[ "$(adb get-state 2>/dev/null || true)" = device ] || { echo "no device attached" >&2; exit 2; }

stop_app() { adb shell am force-stop "$package" >/dev/null 2>&1 || true; }
trap stop_app EXIT INT TERM

if $build; then
  # Never -p:RuntimeIdentifier: it flows into every referenced project and rewrites their lock files.
  dotnet build src/Flyback.Editor.Android -c Release -v minimal --nologo -t:Install -p:AndroidSdkDirectory="$sdk" >&2
fi

activity="$(adb shell cmd package resolve-activity --brief "$package" 2>/dev/null | tr -d '\r' | tail -n 1)"
[[ "$activity" == "$package"/* ]] || { echo "$package is not installed" >&2; exit 1; }

# One line of readings: skin, cpu, battery (C), throttling status, power (mW), then each cluster's clock (MHz).
read_phone() {
  adb shell '
    t="$(dumpsys thermalservice 2>/dev/null)"
    status="$(printf "%s\n" "$t" | sed -n "s/.*Thermal Status: \([0-9]*\).*/\1/p" | head -n 1)"
    hal="$(printf "%s\n" "$t" | sed -n "/Current temperatures from HAL/,/Current cooling devices/p")"
    skin="$(printf "%s\n" "$hal" | grep "mType=3," | sed -n "s/.*mValue=\([0-9.]*\).*/\1/p" | sort -n | tail -n 1)"
    cpu="$(printf "%s\n" "$hal" | grep "mType=0," | sed -n "s/.*mValue=\([0-9.]*\).*/\1/p" | sort -n | tail -n 1)"
    battery="$(dumpsys battery 2>/dev/null | sed -n "s/^ *temperature: \([0-9]*\).*/\1/p")"
    current="$(cat /sys/class/power_supply/battery/current_now 2>/dev/null)"
    voltage="$(cat /sys/class/power_supply/battery/voltage_now 2>/dev/null)"
    clocks=""
    for p in /sys/devices/system/cpu/cpufreq/policy*; do clocks="$clocks $(cat $p/scaling_cur_freq 2>/dev/null)"; done
    echo "$skin $cpu $battery $status $current $voltage$clocks"
  ' 2>/dev/null | tr -d '\r' | awk '{
    battery = $3 / 10
    power = ($5 < 0 ? -$5 : $5) * $6 / 1e9
    line = sprintf("%s,%s,%.1f,%s,%.0f", $1, $2, battery, $4, power)
    for (i = 7; i <= NF; i++) line = line sprintf(",%.0f", $i / 1000)
    print line
  }'
}

skin_now() { read_phone | cut -d, -f1; }
above() { awk -v a="$1" -v b="$2" 'BEGIN { exit !(a > b) }'; }

# The editor's answer to the broadcast; PLAY presses Play before answering.
ask() { adb shell am broadcast -a "$package.${1:-STATE}" -p "$package" 2>/dev/null | tr -d '\r' | sed -n 's/.*data="\(.*\)"$/\1/p'; }
field() { printf '%s' "$2" | sed -n -e "s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" -e t -e "s/.*\"$1\":\([^,}]*\).*/\1/p"; }

clusters="$(adb shell 'ls -d /sys/devices/system/cpu/cpufreq/policy*' 2>/dev/null | tr -d '\r' | sed 's|.*/||' | tr '\n' ',' | sed 's/,$//')"
echo "cores,second,skin,cpu,battery,status,power_mw,${clusters//policy/mhz_cpu},sound_speed,sound_cores" > "$out"
echo "device: $(adb shell getprop ro.product.model | tr -d '\r'); log: $out" >&2

stopped=false

for cores in $counts; do
  skin="$(skin_now)"
  [ -n "$skin" ] || { echo "this phone does not report a skin temperature, so nothing would stop a run that gets hot" >&2; exit 1; }

  waited=0
  until skin="$(skin_now)" && ! above "$skin" "$cool"; do
    [ $waited -lt 600 ] || { echo "the skin stayed above $cool C for ten minutes; stopping" >&2; exit 1; }
    echo "skin ${skin:-unknown} C, waiting to cool to $cool C" >&2
    sleep 15
    waited=$(( waited + 15 ))
  done

  stop_app
  adb shell am start -W -n "$activity" --es preset "$preset" --ei cores "$cores" >/dev/null

  state=""
  for _ in $(seq 1 60); do
    state="$(ask)"
    [ -n "$state" ] && [ -n "$(field soundRunsOn "$state")" ] && [ "$(field soundRunsOn "$state")" != null ] && break
    sleep 1
  done
  ask PLAY >/dev/null

  played=$SECONDS

  # By the clock, not by samples: a sample takes most of a second, and the phone is only asked for so long.
  while [ $(( SECONDS - played )) -lt "$seconds" ]; do
    second=$(( SECONDS - played + 1 ))
    started=$SECONDS
    readings="$(read_phone)"
    state="$(ask)"
    echo "$cores,$second,$readings,$(field soundSpeed "$state"),$(field soundCores "$state")" >> "$out"

    if above "$(printf '%s' "$readings" | cut -d, -f1)" "$hot"; then
      stop_app
      echo "skin passed $hot C on $cores cores at ${second}s; stopped" >&2
      stopped=true
      break 2
    fi

    [ $(( SECONDS - started )) -ge 1 ] || sleep 1
  done

  stop_app
done

# One line per run, skipping the first five seconds while the patch warms up.
awk -F, 'NR > 1 && $2 > 5 {
  n[$1]++; power[$1] += $7; speed[$1] += $(NF - 1); used[$1] = $NF
  if (!($1 in first)) first[$1] = $3
  last[$1] = $3; if ($3 > top[$1]) top[$1] = $3
}
END {
  for (c in n) printf "%s cores (used %s): %.0f mW, skin %.1f -> %.1f C (top %.1f), soundSpeed %.2fx\n", c, used[c], power[c] / n[c], first[c], last[c], top[c], speed[c] / n[c]
}' "$out" | sort -n >&2

$stopped && exit 1
exit 0
