# Tests

## Read the durations, not just the result

A test run that passes still has something to say. After running the suite, rank
the tests by how long each took and look at the top of the list:

```bash
./tests/Flyback.App.Tests/bin/Release/net10.0/Flyback.App.Tests.exe -xml results.xml
```

The runner takes `-xml` directly, and the file has a `time` on every `<test>`.
`dotnet test` prints a duration per assembly and per failure, which is not the
same thing and will not show you a passing test that costs thirty seconds.

The shape to compare against: about 6900 tests in Flyback.App.Tests, a median of
under a millisecond, and a slowest test of a few seconds. Against that, anything
past a second is an outlier worth explaining, and the fifteen slowest are a third
of the whole run.

## A slow test is usually telling you something is wrong

Treat an unexplained outlier as a defect rather than a cost. Almost always it is
one of these:

- **It waits on a wall clock.** A spin-wait with a deadline takes as long as
  whatever it is waiting for, and its deadline when that never arrives. The
  gallery's audition test sat at sixty seconds because a dwell timer, a compile
  and a sound device all had to happen on the one UI thread headless gives the
  whole assembly — the duration was the bug showing itself before the failure
  did.
- **It waits out a deadline to prove a negative.** Nothing arriving is only
  provable by waiting, so cap that wait hard and say in a comment why it is
  short. `A_patch_with_no_picture_leaves_the_tile_alone` waits two seconds
  because the patch is turned away before a renderer is ever built; it was
  thirty until the deadline was made an argument.
- **It leaves something running.** Work a test starts and does not stop is
  charged to every test after it, so the cost shows up spread across the run
  rather than on the test that caused it (see `UiTest`, which closes the windows
  a test opened).
- **It redoes shared work.** Something built once per test that could be built
  once for the class.

Some tests are honestly slow — rendering frames takes as long as it takes. Those
keep their time and say why in a comment. Everything else gets made fast.

## Fix it when you find it

Not a note for later. An outlier found while running the suite for another reason
is fixed in a commit of its own, before or after the work in hand but not inside
it.

## A feature ships with a scenario

Every new feature gets at least one Gherkin scenario in `tests/Flyback.Core.Specs`,
in the same commit as the feature. C# tests still cover the edges; the scenario
states the requirement.

The scenario reads as a business requirement, not a script of actions. It says
what someone patching or playing Flyback can rely on, in their words, and leaves
the wiring, port indexes and op codes to the step definitions.

```gherkin
# Yes: the requirement
Scenario: Turning a tone's frequency while it plays does not click
  Given a 10 Hz sine is playing
  When it has played 0.125 seconds
  And its frequency is turned to 12 Hz
  And it plays on for 0.1 seconds
  Then the sound never clicks

# No: the mechanics
Scenario: Phase is adopted across a recompile
  Given "tone" output "out" is wired to "screen" input "left"
  And "screen" input "volume" is set to 1
  When the sound plays for 125 samples
  Then no two neighboring samples differ by more than 0.08
```

**Why:** a scenario is the one test a reader checks against what Flyback is meant
to do. Written as wiring, it only restates the code, and nobody can tell from it
whether the behavior is the right one.

**How to apply:** name the feature file and the scenarios after what the user
gets. Add the phrase to `PatchSteps` if it builds or edits a patch, or to `ScreenSteps`,
`SpeakerSteps` or `CompilerSteps` if it checks one, and keep the numbers there
unless the number is the requirement ("peaks at a quarter of a second"). A
feature the specs project cannot reach (the editor, a plugin) takes its scenario
where it can be reached, or says in the commit why it has none.
