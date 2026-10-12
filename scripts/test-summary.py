# Writes a test run's summary to stdout: the counts per project, each failing test
# with its message and stack, and each skipped test with its reason. Reads the
# JUnit reports and the exit code from test-results/, as gate.sh and hosted-tests.yml leave them.
import glob, html, os, xml.etree.ElementTree as ET

LISTED = 50
TEXT = 4000

exit_code = open("test-results/exit-code").read().strip()
projects = {}
failures = []
skips = []

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
            elif (skip := case.find("skipped")) is not None:
                counts[2] += 1
                skips.append((project, case.get("name"), skip.get("message") or skip.text or ""))
            else:
                counts[0] += 1

passed = sum(c[0] for c in projects.values())
failed = sum(c[1] for c in projects.values())
skipped = sum(c[2] for c in projects.values())

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

print("| Project | Failed | Passed | Skipped |")
print("|---|---|---|---|")
for project, (p, f, s) in sorted(projects.items()):
    print(f"| {project} | {f} | {p} | {s} |")
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
