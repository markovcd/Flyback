# Runs /mutate's mutants: plants each one, builds and runs the test projects that
# own its file, and puts the file back. One call does the whole pass.
#
#   python3 scripts/mutate.py MUTANTS.json [options]
#   python3 scripts/mutate.py --file F --from OLD --to NEW [options]
#
# MUTANTS.json is a list of {"name", "file", "from", "to"}: "from" must appear in
# "file" exactly once, and is replaced by "to". Every check is made before the first
# mutant is planted, and no file is touched that has uncommitted changes.
#
#   --project NAME    a test project to run besides the owning one (Flyback.Specs);
#                     repeatable. The owning project is tests/<source project>.Tests,
#                     else tests/Flyback.Plugins.Tests for a plugin.
#   --only            runs only the --project projects, not the owning ones
#   --class NAME      passed to each runner as -class; repeatable
#   --method NAME     passed to each runner as -method; repeatable
#   --no-baseline     skips the clean run first (the default refuses a red suite)
#   --out DIR         where the logs go; default $TMPDIR/flyback-mutate/<time>
#   --json            prints the report as JSON
#
# A mutant is caught when a test fails or a test project fails to build, survived
# when every test passes, and rejected when the source itself does not compile: the
# compiler caught it, not a test, so it is left out of the count. A project whose
# filters match no test is reported; a mutant no test ran against is an error.
# The source is built once more at the end, so the binaries match the files.
# Exits 0 once it has reported, 1 on a bad argument, 2 on a red baseline or a run
# that tested nothing.
import argparse, json, os, re, subprocess, sys, tempfile, time

root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
ansi = re.compile(r"\x1b\[[0-9;]*m")


def fail(message, code=1):
    print(f"mutate: {message}", file=sys.stderr)
    sys.exit(code)


parser = argparse.ArgumentParser(add_help=True)
parser.add_argument("mutants", nargs="?")
parser.add_argument("--file")
parser.add_argument("--from", dest="old")
parser.add_argument("--to", dest="new")
parser.add_argument("--project", action="append", default=[])
parser.add_argument("--only", action="store_true")
parser.add_argument("--class", dest="classes", action="append", default=[])
parser.add_argument("--method", dest="methods", action="append", default=[])
parser.add_argument("--no-baseline", action="store_true")
parser.add_argument("--out")
parser.add_argument("--json", action="store_true")
args = parser.parse_args()

if args.mutants and args.file:
    fail("give a mutants file or --file, not both")
if args.mutants:
    with open(args.mutants, encoding="utf-8") as source:
        mutants = json.load(source)
elif args.file and args.old is not None and args.new is not None:
    mutants = [{"name": "M1", "file": args.file, "from": args.old, "to": args.new}]
else:
    fail("give a mutants file, or --file with --from and --to")

if not isinstance(mutants, list) or not mutants:
    fail("the mutants file holds no list of mutants")


def project_of(path):
    """The test project that owns a source file, or None."""
    parts = path.replace("\\", "/").split("/")
    if len(parts) < 3 or parts[0] != "src":
        return None
    name = parts[2] if parts[1] == "plugins" else parts[1]
    for candidate in (f"{name}.Tests", "Flyback.Plugins.Tests" if parts[1] == "plugins" else None):
        if candidate and os.path.isdir(os.path.join(root, "tests", candidate)):
            return candidate
    return None


originals = {}
for index, mutant in enumerate(mutants):
    mutant.setdefault("name", f"M{index + 1}")
    for key in ("file", "from", "to"):
        if not isinstance(mutant.get(key), str):
            fail(f"{mutant['name']} has no \"{key}\"")
    path = mutant["file"].replace("\\", "/")
    mutant["file"] = path
    full = os.path.join(root, path)
    if not os.path.isfile(full):
        fail(f"{mutant['name']}: no file {path}")
    if path not in originals:
        dirty = subprocess.run(["git", "status", "--porcelain", "--", path], cwd=root, capture_output=True, text=True).stdout
        if dirty.strip():
            fail(f"{path} has uncommitted changes; commit or set them aside first")
        with open(full, "rb") as source:
            originals[path] = source.read()
    text = originals[path].decode("utf-8")
    count = text.count(mutant["from"])
    if count != 1:
        fail(f"{mutant['name']}: \"from\" appears {count} times in {path}, not once")
    if mutant["from"] == mutant["to"]:
        fail(f"{mutant['name']}: \"from\" and \"to\" are the same")
    owner = project_of(path)
    mutant["projects"] = list(dict.fromkeys(([] if args.only or owner is None else [owner]) + args.project))
    if not mutant["projects"]:
        fail(f"{mutant['name']}: no test project owns {path}; name one with --project")

for project in {p for m in mutants for p in m["projects"]}:
    if not os.path.isfile(os.path.join(root, "tests", project, f"{project}.csproj")):
        fail(f"no test project tests/{project}")

out = args.out or os.path.join(tempfile.gettempdir(), "flyback-mutate", time.strftime("%Y%m%d-%H%M%S"))
os.makedirs(out, exist_ok=True)


def restore():
    for path, content in originals.items():
        with open(os.path.join(root, path), "wb") as target:
            target.write(content)


def build(project, log):
    with open(log, "a", encoding="utf-8") as sink:
        done = subprocess.run(["dotnet", "build", f"tests/{project}", "-c", "Debug", "-v", "q", "--nologo"],
                              cwd=root, stdout=sink, stderr=subprocess.STDOUT)
    if done.returncode == 0:
        return "built"
    with open(log, encoding="utf-8", errors="replace") as source:
        errors = [line for line in source if ": error " in line]
    in_source = any(re.search(r"[\\/]src[\\/]", line.split(": error ")[0]) for line in errors)
    return "rejected" if in_source else "broke"


def run(project, log):
    """Runs one test project; returns (total, failed test names)."""
    runner = os.path.join(root, "tests", project, "bin", "Debug", "net10.0", project)
    if os.name == "nt":
        runner += ".exe"
    command = [runner] + [a for c in args.classes for a in ("-class", c)] + [a for m in args.methods for a in ("-method", m)]
    with open(log, "w", encoding="utf-8") as sink:
        subprocess.run(command, cwd=root, stdout=sink, stderr=subprocess.STDOUT)
    with open(log, encoding="utf-8", errors="replace") as source:
        lines = [ansi.sub("", line).rstrip() for line in source]
    totals = [re.search(r"Total: (\d+)", line) for line in lines if " Total: " in line]
    total = int(totals[-1].group(1)) if totals and totals[-1] else 0
    failed = [line.strip()[:-len(" [FAIL]")] for line in lines if line.endswith("[FAIL]")]
    return total, failed


def judge(name, projects):
    """Builds and runs the projects as the files stand; returns the verdict."""
    log = os.path.join(out, f"{name}.build.log")
    open(log, "w").close()
    for project in projects:
        built = build(project, log)
        if built == "rejected":
            return {"verdict": "rejected", "failed": [], "empty": []}
        if built == "broke":
            return {"verdict": "caught", "failed": [f"{project} did not build"], "empty": []}
    failed, empty = [], []
    for project in projects:
        total, names = run(project, os.path.join(out, f"{name}.{project}.log"))
        if total == 0:
            empty.append(project)
        failed += [f"{project}: {n}" for n in names]
    if len(empty) == len(projects):
        return {"verdict": "untested", "failed": [], "empty": empty}
    return {"verdict": "caught" if failed else "survived", "failed": failed, "empty": empty}


results = []
try:
    if not args.no_baseline:
        everything = list(dict.fromkeys(p for m in mutants for p in m["projects"]))
        print(f"baseline: {', '.join(everything)}", file=sys.stderr)
        base = judge("baseline", everything)
        if base["verdict"] != "survived":
            fail(f"the suite is not green before any mutant ({base['verdict']}: {'; '.join(base['failed'][:5]) or ', '.join(base['empty'])}); logs in {out}", 2)

    for mutant in mutants:
        path = mutant["file"]
        print(f"{mutant['name']}: {path}", file=sys.stderr)
        text = originals[path].decode("utf-8")
        with open(os.path.join(root, path), "wb") as target:
            target.write(text.replace(mutant["from"], mutant["to"], 1).encode("utf-8"))
        try:
            outcome = judge(mutant["name"], mutant["projects"])
        finally:
            restore()
        results.append({"name": mutant["name"], "file": path, **outcome})
finally:
    restore()
    for project in dict.fromkeys(p for m in mutants for p in m["projects"]):
        build(project, os.path.join(out, "restored.build.log"))

counted = [r for r in results if r["verdict"] in ("caught", "survived")]
report = {
    "planted": len(counted),
    "caught": sum(r["verdict"] == "caught" for r in counted),
    "survived": sum(r["verdict"] == "survived" for r in counted),
    "rejected": sum(r["verdict"] == "rejected" for r in results),
    "untested": sum(r["verdict"] == "untested" for r in results),
    "logs": out,
    "mutants": results,
}

if args.json:
    print(json.dumps(report, indent=2))
else:
    for r in results:
        print(f"{r['name']:<6} {r['verdict']:<9} {r['file']}")
        for name in r["failed"][:5]:
            print(f"         {name}")
        for project in r["empty"]:
            print(f"         {project} ran no test")
    print(f"{report['planted']} planted, {report['caught']} caught, {report['survived']} survived"
          f"; {report['rejected']} rejected by the compiler. Logs in {out}")

sys.exit(2 if report["untested"] else 0)
