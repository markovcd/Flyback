---
description: Plant plausible bugs in the lines a commit changed, see which the tests miss, write the tests that catch them, and log the score.
---

# Mutate

A test suite is only as good as the bugs it catches. Plant a few bugs a person
would plausibly write in the code a commit just changed, run the tests that own
that code, and count what got through. Every survivor is a missing test: write
it. The mutants are thrown away; the tests and one line in
`tests/mutation-log.tsv` are what the run leaves.

$ARGUMENTS names the commit to mutate (`HEAD` when empty) and optionally how
many mutants to plant (`HEAD 8`; five when not given).

## The shape of a run

1. **Start clean and green.** `git status --short` must show nothing this run
   would touch; another session's files are left alone (`git-workflow.md`).
   Build and run the test projects that own the commit's `src/` files. A run on
   a red suite measures nothing.
2. **Read the diff.** `git show -U0 <commit> -- src` gives the changed lines. Only
   those lines are candidates. A commit with no `src/` change logs nothing and
   says so.
3. **Read the scenarios that drive it.** Find the `.feature` files under
   `tests/Flyback.Specs/Features/` whose steps reach the changed code (search
   for the controls, modules or commands it touches). Each scenario is a promise
   the change has to keep; list them. Code that no scenario drives is said so in
   the report: a feature that shipped without the scenario the `tests` skill
   asks for.
4. **Plant one mutant at a time.** Each is a single edit to one changed line, of
   the kind a person gets wrong: a bound off by one, `<` for `<=`, a sign, a
   swapped pair of arguments, a dropped guard or clamp, a wrong default, a
   branch inverted, a socket index shifted, an early `return` lost. Prefer the
   bug that would hurt a user over the one that is easy to write, and first the
   one that would break a scenario's promise. Spread them over the diff rather
   than five on one line.
5. **Skip the equivalent ones.** A mutant that cannot change any behavior (a
   reordered commutative sum, a dead branch) is discarded before running, and is
   not counted. When unsure, run it and read why it survived.
6. **Run them with `scripts/mutate.py`.** Write the mutants to a JSON list of
   `{"name", "file", "from", "to"}`, each `from` a snippet found once in its file,
   and run them in one call. It refuses a red suite, plants each mutant alone,
   builds and runs the test project named for the source project
   (`src/Flyback.Core` → `tests/Flyback.Core.Tests`), puts the file back, and
   says caught, survived or rejected. Add `--project` for any other project that
   tests that code (`Flyback.Specs` for anything a scenario drives), and
   `--class` to narrow the run while choosing. A mutant the compiler rejects is
   not counted.
7. **Kill the survivors.** A survivor that breaks a rule a scenario already
   states is killed in that scenario: a `Then` step saying the rule, so the
   requirement reads whole and `Flyback.Specs` checks it. Any other survivor
   gets a test in the class that owns the behavior, named for the rule rather
   than the mutant. Confirm it fails with the
   mutant applied and passes without it. A survivor not worth a test (logging,
   a message's wording) is said so in the report and still counts as survived.
8. **Log it.** Append one row to `tests/mutation-log.tsv`, tab-separated:
   the commit's short hash, today's date, mutants planted, mutants caught before
   this run's tests, and tests added. `scripts/mutation-score.py` reads it.
9. **Commit the tests and the row** in one commit of their own, after the full
   suites pass. No mutant is ever committed: `git diff` against the commit
   must show only tests and the log.

## Running the pass

```bash
python3 scripts/mutate.py mutants.json --project Flyback.Specs
```

`--json` gives the report to a script, and the logs of every run are kept in the
folder the report names. To check that a new test kills its mutant, run that
mutant alone with `--method` naming the test: it should come back caught.

## Reporting

Lead with the score: planted, caught, survived. Then the scenarios that drive
the changed code, or that none does. Per survivor, the mutant as a
one-line diff, why the tests missed it, and the step or test that now catches it. Then
the rolling score from `python3 scripts/mutation-score.py`.
