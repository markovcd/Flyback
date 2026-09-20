# Harvesting

Turning specimen output into a ranked list of misses. All of it is mechanical —
the judgment happens afterwards, in `reading-the-signal.md`.

---

## Capture ground truth first, and capture it by running the program

A grep over the source is a guess about how names are declared. Ask the program
instead, wherever you can: a throwaway test, a script, a CLI subcommand. It
enumerates the same set the real system enumerates, including anything generated
or registered at startup, and it cannot drift from the declaration style.

For a .NET project that means a one-shot xunit fact that writes the list to a
file, run and then deleted. For a CLI, a `--help` dump or a completion script.
For a library, importing it and reflecting over the exports.

Fall back to grep only where nothing can be run, and say in the report that the
ground truth was grepped rather than enumerated.

Sort it, one name per line, and keep it. Every agent's output is diffed against
this one file.

---

## Two sources per agent, not one

**Prose.** The tutorials, comments, ADRs and messages it wrote. This is where
calling conventions live, because prose examples are written to be read.

**The worktree diff.** `git -C <their worktree> diff` plus any untracked files.
This is where *use* lives — an identifier the agent actually wired into code
carries argument counts, types and ordering that prose does not.

Harvest both. An agent that only wrote prose is still a valid specimen; an agent
that only wrote code is often the better one.

---

## Extracting candidates

Match the shape of your vocabulary, not English words. Some shapes that work:

```bash
# dotted type ids: color.hsv, space.rotate, feedback.trails
grep -ohrE '[a-z][a-z0-9]*[.][a-z][a-z0-9]*' <sources> | sort -u

# anything called like a function inside a code fence or backticks
grep -ohrE '[a-z][a-zA-Z0-9_]*[(]' <sources> | tr -d '(' | sort -u

# CLI flags
grep -ohrE '[-][-][a-z][a-z0-9-]*' <sources> | sort -u
```

Then subtract the language's own keywords and the project's non-vocabulary
identifiers, or the list drowns in `string.Join` and `if(`. The cheapest filter
is to keep only candidates that *look* like the vocabulary — for a catalogue of
dotted ids, keep only names whose prefix is one of the real categories, plus
bare names that appear inside the project's own example syntax.

---

## The diff

```bash
comm -13 truth.txt seen.txt > misses.txt
```

`comm` needs both sides sorted with the same collation. Sort both with the same
command in the same shell, or it will report phantom misses.

---

## Counting repetition

The count that matters is **how many independent agents** reached for a name, not
how many times it appears. One agent writing `blur` twelve times in one document
is one reach; three agents each writing it once is three.

With a run of three, the scale is short: three of three is a strong finding, two
of three is a reading worth acting on, and one of three is a lead to hold rather
than a result to report as one.

So harvest per agent, into one file each, and count across files:

```bash
cat agent-*/misses.txt | sort | uniq -c | sort -rn
```

Then, as a second and weaker measure, note within one agent whether the name was
used as *the* canonical example — the one it returned to whenever it needed to
name a thing of that kind — or was one entry in a list it was padding. The first
is a strong reach. The second is close to noise.

---

## What to keep, and what to throw away

Keep: `truth.txt`, each agent's `misses.txt`, the ranked count, and the verbatim
quotes for anything you report.

Throw away: the worktrees and branches. They are specimens. Delete them once
harvested, and say in the report that you did.
