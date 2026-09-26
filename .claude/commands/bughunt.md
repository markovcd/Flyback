---
description: Hunt for bugs in Flyback, confirm each one as a failing test, then fix it and file the test where it belongs.
---

# Bug hunt

Find bugs that are actually there. A finding is not a finding until something
fails — a test that goes red, or a command whose output you can paste. Then fix
it, and leave the test behind in the class that owns the behavior.

$ARGUMENTS narrows the hunt to a subsystem when it is given. With nothing, sweep.

## The shape of a run

1. **Start from green.** `dotnet build Flyback.slnx -c Debug` and
   `dotnet test Flyback.slnx -c Debug`. About 4,700 tests, three minutes. A hunt
   that begins on a red suite is chasing somebody else's work.
2. **Probe in a scratch class.** One `BugHunt` class per test project it needs —
   `tests/Flyback.Core.Tests/BugHunt.cs`, `tests/Flyback.Plugins.Tests/BugHunt.cs`.
   Everything lands there first, findings and misses alike.
3. **Confirm before fixing.** Every bug gets a red test or a pasteable command
   before a line of `src/` changes. Reproduce it, then reach for the cause.
4. **Fix, then re-approve.** Run the suite. GLSL snapshots under
   `tests/Flyback.Core.Tests/Compile/snapshots` are Verify's; `mv` each
   `.received.glsl` over its `.verified.glsl` only after reading one diff and
   agreeing with it.
5. **File the tests.** Move each one to the class that owns the behavior, name it
   for the rule rather than the bug, and delete the scratch class. A test called
   `A_chunk_that_lies_about_its_length_costs_nothing` outlives the hunt; one
   called `BugHunt.Probe3` does not.
6. **Changelog.** Under `## Unreleased` → `### Fixes`, one terse bullet per fix,
   and only for what a user would notice — the `changelog` skill. A fix to
   something no release ever shipped gets no bullet.

## Run the tests directly

`dotnet test` hides the failure text. Build, then run the assembly:

```bash
dotnet build tests/Flyback.Core.Tests -c Debug -v q --nologo && ./tests/Flyback.Core.Tests/bin/Debug/net10.0/Flyback.Core.Tests.exe -class "Flyback.Core.Tests.BugHunt"
```

`-method "*Name*"` for one test. `-list tests` to see what is there. The runner
is xunit.v3's own — `--filter-class` is not one of its flags.

## The instruments, by what they have paid

**Totality over the instruction set.** Build a one-op `CompiledPatch` by hand,
feed it every hostile float — 0, ±1, ±ε, ±1e20, ±MaxValue, ±∞, NaN — and assert
nothing throws. ADR-0013 says every op is total; this is that claim, executable.
The same rig run through `IlProgram.Compile` is the IL backend against the
interpreter on values no preset reaches. See `Compile/TotalityTests.cs`.

**Every module, run rather than lowered.** `CompilerInvariants` compiles one of
each; evaluating one of each is a different question, and the plugin catalog —
Forms, Voice, Effects, Mastering — is most of the modules and none of that test.
See `Properties/CompilerInvariants.cs` and `Plugins.Tests/EveryModuleTests.cs`.

**A length in a file is not an allocation.** Every hand-rolled reader —
`WavReader`, `PngReader`, `AviWriter`, `PatchBundle` — takes a size off the wire.
Assert with `GC.GetAllocatedBytesForCurrentThread()` that a twenty-byte file
costs under a megabyte. This is where two of the four came from.

**Reachability, every time.** An op that throws on NaN is only a bug if a patch,
a file or a flag can produce a NaN. Chase it to the user: a corrupt `.wav` named
by a patch reaches `CompileForAudio`, which the editor runs on every keystroke.
A finding you cannot reach is a note, not a fix.

**Flags with no ceiling.** Anything a user types a number into and the program
multiplies. `--size 27000x27000` overflowed an int and surfaced as
`Arithmetic operation resulted in an overflow`.

**CPU against GPU, by reading.** `CompiledPatch.Evaluate` is the specification
(ADR-0035); `IlOps` and `GlslEmitter`'s prelude are transcriptions. Read the
three side by side for one op at a time. The snapshots pin the shader's text, not
its arithmetic, so a guard that differs passes every test in the repo.

## Where the repo is already strong

Ran these, found nothing. Do not spend the hunt here again unless the code moved:

- Mutating valid sources and feeding them to `PatchLanguage.Build` — 20,000 of
  them, no exception. Kept as `Language/MangledSourceTests.cs`.
- Random graph edits with `Patch`, `PatchHistory`, `PatchClipboard` and
  `NodeGroup`, checked for dangling wires, a JSON round trip and undo/redo being
  inverses. Kept as `Properties/EditingInvariants.cs`.
- IL against the interpreter over every preset, both sinks, to the bit —
  `IlProgramTests` already does it.
- `PatchBundle` reads a zip into memory and never writes a path, so no zip-slip.

## The real window

Allowed, and worth it for what headless cannot reach — the GPU path, a preset
that plays, a dialog. The `running-the-app` skill first: wait for another
Flyback to close, mark yours with the red caption, drive only the process you
started, close it when done.

Most of the time the headless Avalonia is faster and surer.
`tests/Flyback.App.Tests/Ui/UiTest.cs` is a real Avalonia with Skia that builds a
whole `MainWindow`; `FileDropTests` shows how to raise the events a window
actually listens for.

## Reporting

Lead with the count and the list. Per bug: one sentence on what is wrong, the
repro as a test name or a shell line, and where it is reachable from. Say plainly
where you looked and found nothing — that is half of what makes the next hunt
cheaper.
