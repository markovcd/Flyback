#!/usr/bin/env bash
# The gate: restore, compile and every test, in the image the release is built from.
# The Build workflow runs this and so can anyone.
#
#   ./scripts/gate.sh
#
# The tests' JUnit reports land in test-results/, with summary.md beside them: the
# counts, and each failing test with its message and stack, or the compile errors
# of a build that never reached the tests. The build's whole log is build.log. On
# GitHub the summary is also the run's. The script fails exactly when the gate stage would, on the
# same run.
set -euo pipefail

cd "$(dirname "$0")/.."

. ./scripts/builder.sh

# Only GitHub's runners write the shared cache; a self-hosted one has its own.
[ "${#cache[@]}" -gt 0 ] && cache+=(--cache-to type=gha,mode=max)

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

"$python" - > test-results/summary.md <<'PY'
import glob, html, os, xml.etree.ElementTree as ET

LISTED = 50
TEXT = 4000

exit_code = open("test-results/exit-code").read().strip()
projects = {}
failures = []

for path in sorted(glob.glob("test-results/*.junit.xml")):
    for suite in ET.parse(path).getroot().iter("testsuite"):
        project = os.path.splitext(os.path.basename(suite.get("name")))[0]
        counts = projects.setdefault(project, [0, 0, 0])
        for case in suite.iter("testcase"):
            failure = case.find("failure")
            if failure is None:
                failure = case.find("error")
            if failure is not None:
                counts[1] += 1
                failures.append((project, case.get("name"), failure.get("message") or "", failure.text or ""))
            elif case.find("skipped") is not None:
                counts[2] += 1
            else:
                counts[0] += 1

passed = sum(c[0] for c in projects.values())
failed = sum(c[1] for c in projects.values())
skipped = sum(c[2] for c in projects.values())

if exit_code == "0":
    print("### Tests passed\n")
    print(f"{passed} passed and {skipped} skipped, in {len(projects)} projects.")
else:
    print("### Tests failed\n")
    print(f"{failed} failed, {passed} passed and {skipped} skipped, in {len(projects)} projects.\n")

    if not failures:
        print(f"The run ended with exit code {exit_code} and no failing test in the reports: "
              "a test host hung or crashed, or a project ran no tests. The log says which.")
        raise SystemExit

    print("| Project | Failed | Passed | Skipped |")
    print("|---|---|---|---|")
    for project, (p, f, s) in sorted(projects.items()):
        if f:
            print(f"| {project} | {f} | {p} | {s} |")
    print()

    for project, name, message, stack in failures[:LISTED]:
        text = (message + "\n\n" + stack).strip()
        if len(text) > TEXT:
            text = text[:TEXT] + "\n…"
        first = message.strip().splitlines()[0] if message.strip() else "failed"
        print(f"<details><summary><b>{html.escape(name)}</b> ({html.escape(project)}): {html.escape(first)}</summary>\n")
        print(f"<pre>{html.escape(text)}</pre>\n</details>\n")

    if len(failures) > LISTED:
        print(f"And {len(failures) - LISTED} more, in the reports under test-results/.")
PY

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  cat test-results/summary.md >> "$GITHUB_STEP_SUMMARY"
fi

# The gate stage is this exit code. Building it again can miss the layer cache and
# test a second time, failing on a run the summary never saw.
exit "$(cat test-results/exit-code)"
