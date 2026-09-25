#!/usr/bin/env bash
# Measures how much of the code the tests run, into coverage/: a folder of Cobertura
# reports per test project, and summary.md, a table of lines and branches per
# assembly for every test but the specs, and the specs' own figure beneath it. The
# Coverage workflow runs this and keeps coverage/; run here, it is the same
# measurement.
#
#   ./coverage.sh
#
# Nothing fails on the figure. The Dockerfile's measured stage says why it is not
# part of the gate.
set -euo pipefail

cd "$(dirname "$0")"

build=(docker buildx build --target coverage --output type=local,dest=coverage)

if [ "${GITHUB_ACTIONS:-}" = true ]; then
  # Reads the Build workflow's cache and writes none of its own, so a run here
  # never evicts what the gate depends on.
  build+=(--cache-from type=gha)
else
  # Built as release.sh builds here, so the gate's layers are shared with it.
  . ./release-key.sh
  build+=(--build-arg RELEASE_PUBLIC_KEY)
fi

rm -rf coverage
"${build[@]}" .

python=python3
"$python" -c '' 2>/dev/null || python=python

# One folder per test project, each report naming every assembly that project
# loaded, so the engine is in all of them. A line is covered if any report covered
# it; adding the reports' totals would count it once per project and call it missed
# wherever another project ran it.
#
# The specs are left out of the sum and given a number of their own: they drive the
# whole program to state requirements, so what they reach says what is specified,
# not what is tested.
"$python" - > coverage/summary.md <<'PY'
import collections, glob, os, re, xml.etree.ElementTree as ET

SPECS = "Flyback.Specs"

def measure(paths):
    hit = {}
    taken = {}

    for path in paths:
        for package in ET.parse(path).getroot().iter("package"):
            for cls in package.iter("class"):
                for line in cls.iter("line"):
                    key = (package.get("name"), cls.get("filename"), line.get("number"))
                    hit[key] = hit.get(key, False) or int(line.get("hits")) > 0

                    conditions = re.search(r"\((\d+)/(\d+)\)", line.get("condition-coverage") or "")
                    if conditions:
                        was = taken.get(key, (0, 0))
                        taken[key] = (max(was[0], int(conditions[1])), max(was[1], int(conditions[2])))

    lines = collections.defaultdict(lambda: [0, 0])
    branches = collections.defaultdict(lambda: [0, 0])

    for (assembly, _, _), covered in hit.items():
        lines[assembly][0] += covered
        lines[assembly][1] += 1

    for (assembly, _, _), (covered, total) in taken.items():
        branches[assembly][0] += covered
        branches[assembly][1] += total

    return lines, branches

def total(pairs):
    return [sum(p[0] for p in pairs.values()), sum(p[1] for p in pairs.values())]

def rate(pair):
    return f"{pair[0] / pair[1]:.1%}" if pair[1] else "n/a"

def row(name, line, branch):
    print(f"| {name} | {line[0]} / {line[1]} | {rate(line)} | {branch[0]} / {branch[1]} | {rate(branch)} |")

reports = sorted(glob.glob("coverage/*/*.cobertura.xml"))
specs = [path for path in reports if os.path.basename(os.path.dirname(path)) == SPECS]
tests = [path for path in reports if path not in specs]

lines, branches = measure(tests)
spec_lines, spec_branches = measure(specs)

print("| | Lines | | Branches | |")
print("|---|---|---|---|---|")
row("**All tests but the specs**", total(lines), total(branches))

for assembly in sorted(lines, key=lambda a: -lines[a][1]):
    row(assembly, lines[assembly], branches[assembly])

print()
print("| | Lines | | Branches | |")
print("|---|---|---|---|---|")
row("**The specs alone**", total(spec_lines), total(spec_branches))
PY

cat coverage/summary.md

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  cat coverage/summary.md >> "$GITHUB_STEP_SUMMARY"
fi
