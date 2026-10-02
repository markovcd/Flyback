#!/usr/bin/env bash
# Measures two builds of Flyback on this machine and says what got faster or slower:
# the benchmarks in tests/Flyback.Core.Benchmarks, and with --web the web viewer's
# sound under Node. Each side is built in a worktree of its own, the runs alternate
# between the two so a machine that slows down slows both, and a change is called
# only when it passes a threshold and a Mann-Whitney test (scripts/bench-compare.py).
#
#   ./scripts/bench-compare.sh BASE [HEAD] [options] [-- BenchmarkDotNet arguments]
#
# BASE and HEAD are commits; HEAD defaults to HEAD, and "." is the working tree as it
# stands, uncommitted edits included.
#
#   --filter GLOB     the benchmarks to run, as BenchmarkDotNet's --filter; default '*',
#                     all 65 cases, about nine minutes a side a round
#   --job NAME        BenchmarkDotNet's job: short (default), medium or default
#   --rounds N        runs of each side's benchmarks, alternating; default 3
#   --web PRESETS     presets, comma-separated, whose sound the web viewer's AOT build
#                     plays under Node; needs the wasm-tools workload
#   --web-only        runs no benchmarks, only --web
#   --web-rounds N    runs of each side's web viewer a preset, alternating; default 5,
#                     four being the fewest that can call a change
#   --seconds S       sound each web run plays; default 10
#   --threshold P     the smallest change in percent called a change; default 5
#   --out DIR         where the raw results, the logs and the report go
#   --json            prints the report as JSON rather than a table
#   --clean           removes the worktrees and builds kept from earlier runs, and stops
#
# A commit's worktree and builds are kept under $FLYBACK_BENCH_DIR (default
# $TMPDIR/flyback-bench) and reused, so comparing against the same base again builds
# only the head. Exits 0 once it has reported, 1 on a bad argument, 2 when a build or
# a run fails; which side got slower is in the report, not the exit code.
set -euo pipefail

cd "$(dirname "$0")/.."
repo="$(pwd)"

cache="${FLYBACK_BENCH_DIR:-${TMPDIR:-/tmp}/flyback-bench}"
base="" head="HEAD" filter="*" job="short" rounds=3 web="" web_only=false web_rounds=5
seconds=10 threshold=5 out="" as_json=false extra=()

fail() { echo "bench-compare: $*" >&2; exit 1; }

# A number typed here is multiplied into minutes of runs, so each has a ceiling.
whole() { [[ "$2" =~ ^[0-9]+$ ]] && (( $2 >= 1 && $2 <= $3 )) || fail "$1 takes a whole number from 1 to $3, not '$2'"; }

positional=()
while (( $# > 0 )); do
  case "$1" in
    --filter) filter="${2:?--filter takes a glob}"; shift 2 ;;
    --job) job="${2:?--job takes a name}"; shift 2 ;;
    --rounds) whole --rounds "${2:-}" 20; rounds="$2"; shift 2 ;;
    --web) web="${2:?--web takes presets}"; shift 2 ;;
    --web-only) web_only=true; shift ;;
    --web-rounds) whole --web-rounds "${2:-}" 20; web_rounds="$2"; shift 2 ;;
    --seconds) whole --seconds "${2:-}" 120; seconds="$2"; shift 2 ;;
    --threshold) [[ "${2:-}" =~ ^[0-9]+(\.[0-9]+)?$ ]] || fail "--threshold takes a percentage"; threshold="$2"; shift 2 ;;
    --out) out="${2:?--out takes a folder}"; shift 2 ;;
    --json) as_json=true; shift ;;
    --clean)
      for dir in "$cache"/*/; do
        [ -d "$dir" ] && git worktree remove --force "$dir" 2>/dev/null || true
      done
      rm -rf "$cache"
      git worktree prune
      echo "bench-compare: removed $cache"
      exit 0 ;;
    --) shift; extra=("$@"); break ;;
    -h|--help) sed -n '2,/^set -euo/p' "$0" | sed '$d; s/^# \{0,1\}//'; exit 0 ;;
    -*) fail "no option $1; --help lists them" ;;
    *) positional+=("$1"); shift ;;
  esac
done

(( ${#positional[@]} >= 1 && ${#positional[@]} <= 2 )) || fail "name a base commit, and a head if not HEAD; --help says more"
base="${positional[0]}"
[ ${#positional[@]} -eq 2 ] && head="${positional[1]}"
case "$job" in short|medium|default|long) ;; *) fail "--job is short, medium, default or long" ;; esac
$web_only && [ -z "$web" ] && fail "--web-only needs --web"

python=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null && "$candidate" -c 'import sys; sys.exit(sys.version_info < (3, 8))' 2>/dev/null; then
    python="$candidate"; break
  fi
done
[ -n "$python" ] || fail "needs Python 3.8 or later to compare the results"

# A commit by its hash, or "." for the working tree.
resolve() {
  if [ "$1" = . ]; then echo .; return; fi
  git rev-parse --verify --quiet "$1^{commit}" || fail "no commit '$1'"
}

base_sha="$(resolve "$base")"
head_sha="$(resolve "$head")"
[ "$base_sha" = "$head_sha" ] && fail "base and head are the same commit"
label() { [ "$1" = . ] && echo "working tree" || git log -1 --format='%h %s' "$1" | cut -c1-72; }

mkdir -p "$cache"
[ -n "$out" ] || out="$cache/runs/$(date '+%Y%m%d-%H%M%S')-${base_sha:0:8}-${head_sha:0:8}"
mkdir -p "$out/raw/base" "$out/raw/head"
printf '{"base": "%s", "head": "%s"}\n' "$(label "$base_sha" | sed 's/["\\]//g')" "$(label "$head_sha" | sed 's/["\\]//g')" > "$out/meta.json"

# The checkout a side builds in: the working tree itself, or a worktree of the commit kept for next time.
checkout() {
  if [ "$1" = . ]; then echo "$repo"; return; fi

  local dir="$cache/${1:0:12}"

  if [ ! -d "$dir" ] || [ "$(git -C "$dir" rev-parse HEAD 2>/dev/null)" != "$1" ]; then
    rm -rf "$dir"
    git worktree prune
    # Some snapshot names pass Windows' 260 characters under a deep temporary folder.
    git -c core.longpaths=true worktree add --detach "$dir" "$1" > "$out/worktree-${1:0:12}.log" 2>&1 \
      || { cat "$out/worktree-${1:0:12}.log" >&2; exit 2; }
  fi

  echo "$dir"
}

# Builds what a side runs, once per commit: a commit never changes, the working tree always may.
build() {
  local side="$1" sha="$2" what="$3" dir marker log
  dir="$(checkout "$sha")"
  [ -d "$dir" ] || exit 2
  marker="$dir/.bench-built-$what"
  log="$out/build-$side-$what.log"

  if [ "$sha" != . ] && [ -f "$marker" ]; then return; fi

  echo "bench-compare: building the $what of $side ($(label "$sha"))" >&2

  case "$what" in
    benchmarks) (cd "$dir" && dotnet build tests/Flyback.Core.Benchmarks -c Release) > "$log" 2>&1 ;;
    web) (cd "$dir" && dotnet publish src/Flyback.Viewer.Web -c Release -p:RunAOTCompilation=true -o "$(web_dir "$sha")") > "$log" 2>&1 ;;
  esac || { tail -20 "$log" >&2; echo "bench-compare: the $what of $side did not build; the whole log is $log" >&2; exit 2; }

  [ "$sha" != . ] && touch "$marker"
  return 0
}

web_dir() { [ "$1" = . ] && echo "$cache/working-tree-web" || echo "$cache/${1:0:12}/.bench-web"; }

side_sha() { [ "$1" = base ] && echo "$base_sha" || echo "$head_sha"; }

# Each round starts with the side the last one ended on, so neither always goes first.
order() { (( $1 % 2 )) && echo "base head" || echo "head base"; }

if ! $web_only; then
  for side in base head; do build "$side" "$(side_sha "$side")" benchmarks; done

  for (( round = 1; round <= rounds; round++ )); do
    for side in $(order "$round"); do
      echo "bench-compare: benchmarks, round $round of $rounds, $side" >&2
      exe="$(checkout "$(side_sha "$side")")/tests/Flyback.Core.Benchmarks/bin/Release/net10.0/Flyback.Core.Benchmarks"
      [ -f "$exe.exe" ] && exe="$exe.exe"
      log="$out/run-$side-r$round.log"

      # In process: BenchmarkDotNet's own toolchain looks for the project across the
      # whole clone and refuses on finding a copy in each of .claude/worktrees.
      "$exe" --filter "$filter" --job "$job" --inProcess --memory --exporters json \
        --artifacts "$out/raw/$side/r$round" ${extra[@]+"${extra[@]}"} > "$log" 2>&1 \
        || { tail -20 "$log" >&2; echo "bench-compare: the benchmarks of $side failed; the whole log is $log" >&2; exit 2; }
    done
  done
fi

if [ -n "$web" ]; then
  if ! dotnet workload list 2>/dev/null | grep -q wasm-tools; then
    fail "--web needs the wasm-tools workload: dotnet workload install wasm-tools"
  fi

  node="$(command -v node || true)"
  if [ -z "$node" ]; then
    packs="$(dirname "$(command -v dotnet)")/packs"
    node="$(find "$packs" -path '*Emscripten*Node*' \( -name node -o -name node.exe \) -type f 2>/dev/null | head -1)"
  fi
  [ -n "$node" ] || fail "--web needs Node, on PATH or from the wasm-tools workload"

  for side in base head; do build "$side" "$(side_sha "$side")" web; done

  IFS=',' read -ra presets <<< "$web"

  for (( round = 1; round <= web_rounds; round++ )); do
    for side in $(order "$round"); do
      mkdir -p "$out/raw/$side/web"
      hear="$(web_dir "$(side_sha "$side")")/hear.mjs"

      for preset in "${presets[@]}"; do
        preset="$(echo "$preset" | sed 's/^ *//; s/ *$//')"
        echo "bench-compare: web viewer, round $round of $web_rounds, $side, $preset" >&2
        file="$out/raw/$side/web/r$round-$(echo "$preset" | tr -c 'A-Za-z0-9\n' '_').json"

        "$node" "$hear" --preset "$preset" --seconds "$seconds" > "$file" 2> "$file.log" \
          || { cat "$file.log" >&2; echo "bench-compare: the web viewer of $side could not play '$preset'" >&2; exit 2; }
      done
    done
  done
fi

"$python" scripts/bench-compare.py "$out" "$threshold" $($as_json && echo --json)
echo "bench-compare: raw results, logs and report.md are in $out" >&2
