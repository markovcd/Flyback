# The share of /mutate's mutants the tests caught, over the latest runs in
# tests/mutation-log.tsv. Caught counts what the tests caught before the run
# added any, so it scores the tests a commit arrived with.
#
#   python3 scripts/mutation-score.py [--window 50] [--json]
import argparse, csv, json, os

parser = argparse.ArgumentParser()
parser.add_argument("--window", type=int, default=50, help="how many of the latest runs to score")
parser.add_argument("--json", action="store_true")
args = parser.parse_args()

log = os.path.join(os.path.dirname(__file__), "..", "tests", "mutation-log.tsv")
with open(log, newline="") as file:
    rows = list(csv.DictReader(file, delimiter="\t"))

latest = rows[-args.window:] if args.window > 0 else rows
planted = sum(int(row["planted"]) for row in latest)
caught = sum(int(row["caught"]) for row in latest)
added = sum(int(row["tests_added"]) for row in latest)
score = caught / planted if planted else None

if args.json:
    print(json.dumps({"runs": len(latest), "planted": planted, "caught": caught,
                      "survived": planted - caught, "tests_added": added, "score": score}))
elif score is None:
    print("No mutants logged yet.")
else:
    print(f"{caught} of {planted} mutants caught ({score:.0%}) over the latest {len(latest)} runs; "
          f"{planted - caught} survived, and the runs added {added} tests.")
