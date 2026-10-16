# Writes a test run's summary to stdout: the counts per project, each failing test
# with its message and stack, and each skipped test with its reason. Reads the
# JUnit reports and the exit code from test-results/, as gate.sh and cloud-gate.yml leave them.
# When the run's wall-clock seconds are there too, it holds them to the budget below
# and warns past it, slowest project first.
import glob, html, os, sys, xml.etree.ElementTree as ET

LISTED = 50
TEXT = 4000

# Past this the gate is slow enough to tempt anyone to skip it: time to shard it
# by project or find what got slow (docs/agents/rules/pipeline.md).
BUDGET = 600

exit_code = open("test-results/exit-code").read().strip()
seconds = None
if os.path.exists("test-results/seconds"):
    seconds = int(open("test-results/seconds").read().strip())
projects = {}
durations = {}
failures = []
skips = []

for path in sorted(glob.glob("test-results/*.junit.xml")):
    for suite in ET.parse(path).getroot().iter("testsuite"):
        project = os.path.splitext(os.path.basename(suite.get("name")))[0]
        counts = projects.setdefault(project, [0, 0, 0])
        cases = list(suite.iter("testcase"))
        took = suite.get("time")
        took = float(took) if took else sum(float(c.get("time") or 0) for c in cases)
        durations[project] = durations.get(project, 0) + took
        for case in cases:
            failure = case.find("failure")
            if failure is None:
                failure = case.find("error")
            if failure is not None:
                counts[1] += 1
                failures.append((project, case.get("name"), failure.get("message") or "", failure.text or ""))
            elif (skip := case.find("skipped")) is not None:
                counts[2] += 1
                skips.append((project, case.get("name"), skip.get("message") or skip.text or ""))
            else:
                counts[0] += 1

passed = sum(c[0] for c in projects.values())
failed = sum(c[1] for c in projects.values())
skipped = sum(c[2] for c in projects.values())

def clock(total):
    total = round(total)
    return f"{total // 60}:{total % 60:02d}"

def cell(text):
    return html.escape(" ".join(text.split())).replace("|", "\\|")

def list_skips():
    if not skips:
        return
    print("| Project | Skipped | Reason |")
    print("|---|---|---|")
    for project, name, reason in sorted(skips):
        print(f"| {cell(project)} | {cell(name)} | {cell(reason)} |")

if exit_code == "0":
    print("### Tests passed\n")
    print(f"{passed} passed and {skipped} skipped, in {len(projects)} projects.\n")
else:
    print("### Tests failed\n")
    print(f"{failed} failed, {passed} passed and {skipped} skipped, in {len(projects)} projects.\n")

if seconds is not None:
    if seconds > BUDGET:
        over = (f"The tests took {clock(seconds)}, past the gate's {clock(BUDGET)} budget. "
                "Shard the gate by project or speed up the slowest ones below.")
        print(f"**Over budget.** {over}\n")
        print(f"::warning title=Gate time budget::{over}", file=sys.stderr)
    else:
        print(f"The tests took {clock(seconds)} of the gate's {clock(BUDGET)} budget.\n")

print("| Project | Failed | Passed | Skipped | Time |")
print("|---|---|---|---|---|")
for project, (p, f, s) in sorted(projects.items(), key=lambda item: (-durations[item[0]], item[0])):
    print(f"| {project} | {f} | {p} | {s} | {clock(durations[project])} |")
print()

if exit_code != "0" and not failures:
    print(f"The run ended with exit code {exit_code} and no failing test in the reports: "
          "a test host hung or crashed, or a project ran no tests. The log says which.\n")

for project, name, message, stack in failures[:LISTED]:
    text = (message + "\n\n" + stack).strip()
    if len(text) > TEXT:
        text = text[:TEXT] + "\n…"
    first = message.strip().splitlines()[0] if message.strip() else "failed"
    print(f"<details><summary><b>{html.escape(name)}</b> ({html.escape(project)}): {html.escape(first)}</summary>\n")
    print(f"<pre>{html.escape(text)}</pre>\n</details>\n")

if len(failures) > LISTED:
    print(f"And {len(failures) - LISTED} more, in the reports under test-results/.\n")

list_skips()
