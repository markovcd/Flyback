#!/usr/bin/env bash
# The gate: restore, compile and every test, in the image the release is built from.
# The Build workflow runs this and so can anyone.
#
#   ./scripts/gate.sh
#
# The tests' JUnit reports land in test-results/, with summary.md beside them: the
# counts, and each failing test with its message and stack. On GitHub it is also
# the run's summary.
set -euo pipefail

cd "$(dirname "$0")/.."

. ./scripts/builder.sh

# Only GitHub's runners write the shared cache; a self-hosted one has its own.
[ "${#cache[@]}" -gt 0 ] && cache+=(--cache-to type=gha,mode=max)

# The test run never fails this build, so the reports come out of a failing one.
rm -rf test-results
docker buildx build "${builder[@]}" "${cache[@]}" --target test-results --output type=local,dest=test-results .

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

docker buildx build "${builder[@]}" "${cache[@]}" --target gate .
