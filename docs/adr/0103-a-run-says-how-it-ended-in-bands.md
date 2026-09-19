# ADR-0103: A run says how it ended, in bands

**Status:** Accepted · 2026-09-19 · *user-directed* · amends
[0094](0094-a-run-says-what-it-played-and-nothing-about-who-played-it.md),
whose three events become five and whose "nothing waits" gains one exception

## Context

[0094](0094-a-run-says-what-it-played-and-nothing-about-who-played-it.md) counts
what a run started as and what it played. That answers "does anyone use the
Analyzer" and nothing past the first minute: whether anybody records, saves or
goes full screen; whether a run lasts two minutes or two hours; whether the
picture keeps up on the machines Flyback is actually run on; how many copies are
new rather than updated; and — the one a program should most want to know —
whether it falls over, and where.

The instruction was to gather more, and to keep it from identifying anybody.
The line [0094](0094-a-run-says-what-it-played-and-nothing-about-who-played-it.md)
drew still holds: no id that outlives a run, nothing about a patch but its
shape, nothing typed. What is new is facts about the machine and the evening,
and each of those is a little of a fingerprint. Several precise ones together —
twelve cores, 31.7 GB, a 1600-pixel screen, 47 minutes — begin to single out one
machine even with no id on them.

## Decision

**A figure is sent as the band it falls in, never as itself.** Cores are 1, 2–3,
4–7 and so on to 32+; memory is <2 GB to 64+ GB in doublings; the tallest screen
is <720 to 2880+ pixels; a run's length is <1 to 480+ minutes; how often a thing
was done is 0, 1, 2–4, 5–9 or 10+. A band is shared by a large part of everyone
who runs Flyback, which is what keeps a report anonymous rather than merely
unnamed. The one exception is a played patch's module and wire counts, which
0094 already sends as they are: they are a patch's shape, not the machine's.

**`started` says how the run began and how big the machine is.** Whether there
was no settings folder yet — the first start on this machine, which is the
nearest thing to an install count that needs no id; whether a release Flyback
downloaded itself was installed just before; whether it was launched to open a
file; and the bands for cores, memory, screen count (1, 2 or 3+) and the
tallest screen. Nothing new is written to find these out: the settings folder
and the update note are already there.

**`played` also says how many wires, and which preset.** The preset is named
where it is the engine's own, or a plugin's while every plugin that loaded is
one Flyback ships — a stranger's plugin could offer a preset whose name is the
stranger's own. A patch from a file, or from nothing, says `none`.

**`ended` is new, and comes once, as the window closes.** It carries the run's
length, how many times a patch began to play (every one, not the three 0094
reports), and how often each of these was done: a preset picked, a file opened,
a file saved, a module added, a take recorded to the end, the preview given
the whole window, the preview and canvas swapped, the text view shown, a MIDI
device heard, a message sent to an assistant, the settings opened. With them,
the renderer that drew most of the run and the frame-rate band it spent most
of its time in, sampled from the status bar's own reading while the window is
the one in front.

**`crashed` is new, and says the least a crash can say.** The exception's type
— by name where it is the platform's or Flyback's, `other` where a plugin
somebody wrote threw it — the innermost method of Flyback's own it passed
through, as a type and a method with no file or line, and the run's length.
Never its message, which is where a path and a person's name would be, and never
its trace.

**At most seven events in a run:** `started`, three `played`, `assistant`,
`ended`, `crashed`.

**A run that is ending waits two seconds for its last events.** An event is
still posted on a thread of its own and nothing retries it, but `ended` and
`crashed` are sent as the process leaves, and without a wait neither would ever
arrive. Two seconds is long enough for a request that is going to be answered
and short enough that closing Flyback never feels held.

**`flyback.mastering` joins the list of names Flyback ships.** It was a
shipped plugin being counted as `other`.

## Consequences

**Still nothing to ask to be forgotten.** No report carries anything that joins
it to another run or to a person, the address it came from is not kept (0094),
and every figure is coarse enough to be shared by many machines. That is the
test for data being anonymous rather than pseudonymous, and it is why this can
stay on by default.

**Installs are counted, roughly.** `first` is a start with no settings folder,
so somebody who deletes theirs counts again and somebody who copies theirs to a
new machine does not. It is a count of first starts, and reading it as a count
of people is the mistake 0094 warns about.

**The frame rate is the status bar's,** which is the preview's own rate and is
capped by the preview frame-rate setting. A band of 30–44 can mean a slow
machine or a person who asked for 30.

**A crash in a plugin nobody shipped says `other` twice,** once for the type and
once for the place — where it passed through the shell on its way, the shell's
method is named instead, which is still the right place to start looking.

**The disclosure grows with it.** The Usage tab lists every one of these, as
0094 required, and so does the download page.
