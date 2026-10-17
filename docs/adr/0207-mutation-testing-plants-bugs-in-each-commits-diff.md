# ADR-0207: Mutation testing plants bugs in each commit's diff

**Status:** Accepted · 2026-10-16 · *user-directed*

## Context

Coverage says a line ran, not that a test would notice it going wrong. Mutation
testing answers that by breaking the code and seeing whether a test fails.
Rule-based tools (Stryker.NET) produce many trivial or equivalent mutants and
take a package; an agent writes the bugs a person would. A corpus of saved
mutants would be the repeatable measure, but its patches go stale as soon as the
code they hit moves.

## Decision

**`/mutate` plants a few plausible bugs in the lines one commit changed, and every
survivor gets a test.** The mutants are thrown away. What a run leaves is the
tests, and one row in `tests/mutation-log.tsv`: the commit, mutants planted,
mutants the existing tests caught, and tests added. `scripts/mutation-score.py`
gives the share caught over the latest runs (fifty by default, `--json` for a
script), which is how well new code arrives tested.

This follows Google's diff-based mutation testing: only changed code is mutated,
so nothing goes stale, and a score per commit adds up to a trend.

## Consequences

- One run is a small sample; the rolling score is the number to read.
- Code nobody changes is never mutated. The weekly coverage run still watches it.
- No package: the agent is the mutator and the owning test projects are the judge.
  `scripts/mutate.py` plants each mutant, runs those projects and puts the file back.
- `MutationLogTests` keeps the log in the shape the script reads.
