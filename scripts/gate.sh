#!/usr/bin/env bash
# The gate: restore, compile and every test, in the image the release is built from.
# The Build workflow runs this and so can anyone.
#
#   ./scripts/gate.sh
#
# The tests' JUnit reports land in test-results/, with summary.md beside them: the
# counts per project, each failing test with its message and stack, and each
# skipped test with its reason, or the compile errors of a build that never
# reached the tests. The build's whole log is build.log. On GitHub the summary is
# also the run's. The script fails exactly when the gate stage would, on the same run.
set -euo pipefail

cd "$(dirname "$0")/.."

. ./scripts/builder.sh

# The test run never fails this build, so the reports come out of a failing one.
# What does fail it is a build that stops before the tests: a compile error, a
# locked restore. Its log is kept beside the reports and its errors summarized.
rm -rf test-results
log="$(mktemp)"
set +e
docker buildx build "${builder[@]}" "${cache[@]}" --target test-results --output type=local,dest=test-results . 2>&1 | tee "$log"
built=${PIPESTATUS[0]}
set -e
mkdir -p test-results
mv "$log" test-results/build.log

if [ "$built" -ne 0 ]; then
  errors="$(sed -E 's/^#[0-9]+ [0-9.]+ //' test-results/build.log | grep -E 'error [A-Z]+[0-9]+:|^ERROR' | awk '!seen[$0]++' | head -n 20 || true)"
  {
    printf '### The build failed before the tests ran\n\n'
    if [ -n "$errors" ]; then
      printf '```\n%s\n```\n' "$errors"
    else
      printf 'No error line in the log; its last lines:\n\n```\n%s\n```\n' "$(tail -n 30 test-results/build.log)"
    fi
    printf '\nThe whole log is test-results/build.log.\n'
  } > test-results/summary.md
  if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
    cat test-results/summary.md >> "$GITHUB_STEP_SUMMARY"
  fi
  cat test-results/summary.md >&2
  exit "$built"
fi

python=python3
"$python" -c '' 2>/dev/null || python=python

"$python" scripts/test-summary.py > test-results/summary.md

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  cat test-results/summary.md >> "$GITHUB_STEP_SUMMARY"
fi

# The gate stage is this exit code. Building it again can miss the layer cache and
# test a second time, failing on a run the summary never saw.
exit "$(cat test-results/exit-code)"
