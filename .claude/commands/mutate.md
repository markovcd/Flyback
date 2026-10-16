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
3. **Plant one mutant at a time.** Each is a single edit to one changed line, of
   the kind a person gets wrong: a bound off by one, `<` for `<=`, a sign, a
   swapped pair of arguments, a dropped guard or clamp, a wrong default, a
   branch inverted, a socket index shifted, an early `return` lost. Prefer the
   bug that would hurt a user over the one that is easy to write. Spread them
   over the diff rather than five on one line.
4. **Skip the equivalent ones.** A mutant that cannot change any behavior (a
   reordered commutative sum, a dead branch) is discarded before running, and is
   not counted. When unsure, run it and read why it survived.
5. **Run the owning tests.** The test project named for the source project
   (`src/Flyback.Core` → `tests/Flyback.Core.Tests`), plus any other project that
   tests that code. Caught means at least one test failed or the build broke on
   a test. Record caught or survived, then revert the file with
   `git checkout -- <file>` and confirm `git diff -- src` is empty before the next.
6. **Kill the survivors.** For each, write a test in the class that owns the
   behavior, named for the rule rather than the mutant. Confirm it fails with the
   mutant applied and passes without it. A survivor not worth a test (logging,
   a message's wording) is said so in the report and still counts as survived.
7. **Log it.** Append one row to `tests/mutation-log.tsv`, tab-separated:
   the commit's short hash, today's date, mutants planted, mutants caught before
   this run's tests, and tests added. `scripts/mutation-score.py` reads it.
8. **Commit the tests and the row** in one commit of their own, after the full
   suites pass. No mutant is ever committed: `git diff` against the commit
   must show only tests and the log.

## Run the tests directly

`dotnet test` hides the failure text. Build, then run the assembly:

```bash
dotnet build tests/Flyback.Core.Tests -c Debug -v q --nologo && ./tests/Flyback.Core.Tests/bin/Debug/net10.0/Flyback.Core.Tests -class "Flyback.Core.Tests.Compile.TotalityTests"
```

A mutant only needs the classes that could notice it while choosing; it is
counted against the whole owning project.

## Reporting

Lead with the score: planted, caught, survived. Per survivor, the mutant as a
one-line diff, why the tests missed it, and the test that now catches it. Then
the rolling score from `python3 scripts/mutation-score.py`.
