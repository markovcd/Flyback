Feature: Compiling a patch
  Compilation walks back from the Output node and lowers what it reaches.
  A patch is edited live, so every failure has to degrade into something that
  still renders rather than throwing — the editor must survive a half-built
  graph.

  Specified by ADR-0011, and by ADR-0050 for the sockets that are already
  carrying a signal before anything is patched into them.

  # Only a graph assembled by hand can be in this state — every patch that comes
  # from a preset, a file or the editor carries its Output. Answered with a
  # value rather than a throw all the same: saying what is wrong with a patch is
  # the compiler's job, and refusing to look at one is not.
  Scenario: A patch with no Output at all renders black and says so
    Given a patch containing:
      | name | module   |
      | wave | osc.sine |
    When the patch is compiled
    Then compilation reports an issue containing "no Output"
    And the rendered image is entirely black

  # The ordinary case: the sink is there, as it always is, and nothing reaches
  # it. What compiles is a constant — one flat color and silence — which is a
  # legal program and not a patch anybody meant.
  Scenario: A sink with nothing wired into it is remarked on
    Given a patch containing:
      | name   | module |
      | screen | output |
    When the patch is compiled
    Then compilation reports an issue containing "Nothing is wired into the Output"
    And compilation reports nothing wrong

  Scenario: A sink with something wired into it is not remarked on
    Given a patch containing:
      | name   | module       |
      | knob   | value        |
      | screen | output       |
    And "knob" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues

  # An oscillator accumulates how far its 'in' moved, so one left on a constant
  # holds a single value: silence at the speakers, a flat field on the screen.
  # That was the one mistake a patch could make that read perfectly and did
  # nothing, and the compiler used to be all there was to catch it. Every domain
  # is normalled to Time now, so there is nothing left to catch: an oscillator
  # with nothing plugged in runs, and the clock it runs on is in the program
  # without a Time module anywhere in the patch.
  Scenario: A domain with nothing patched into it is driven by Time
    Given a patch containing:
      | name   | module       |
      | osc    | osc.sine     |
      | screen | output       |
    And "osc" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues
    And the program contains at least one "LoadT" op

  # The knob is not what a normalled socket compiles to, which is the whole of
  # what "normalled" means. Left at a quarter cycle it would have drawn a flat
  # white field for ever; driven by Time it starts at nothing and moves.
  Scenario: A normalled socket ignores the value stored against it
    Given a patch containing:
      | name   | module       |
      | osc    | osc.sine     |
      | screen | output       |
    And "osc" input "in" is set to 0.25
    And "osc" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then the centre pixel is about 0, 0, 0

  # Patching overrides the normal exactly as it overrides a knob. Nothing reads
  # the clock any more, so nothing loads it.
  Scenario: A wire into a normalled socket replaces what was normalled to it
    Given a patch containing:
      | name   | module       |
      | coords | coord        |
      | osc    | osc.sine     |
      | screen | output       |
    And "coords" output "x" is wired to "osc" input "in"
    And "osc" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues
    And the program contains no "LoadT" ops

  # One hidden module however many sockets are normalled to it, which is what
  # keeps this from costing a load per oscillator.
  Scenario: Every socket normalled to Time shares one reading of it
    Given a patch containing:
      | name   | module       |
      | first  | osc.sine     |
      | second | osc.saw      |
      | mix    | math.add     |
      | screen | output       |
    And "first" output "out" is wired to "mix" input "a"
    And "second" output "out" is wired to "mix" input "b"
    And "mix" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then the program contains exactly 1 "LoadT" op

  Scenario: A domain that is driven is not remarked on
    Given a patch containing:
      | name   | module       |
      | clock  | time         |
      | osc    | osc.sine     |
      | screen | output       |
    And "clock" output "t" is wired to "osc" input "in"
    And "osc" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues

  Scenario: An unknown module is reported rather than throwing
    Given a patch containing:
      | name   | module       |
      | screen | output       |
    And a node named "mystery" of unknown type "module.from.the.future"
    And "mystery" output 0 is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports an issue containing "Unknown module"
    And the rendered image is entirely black

  Scenario: A cycle is reported instead of hanging
    Given a patch containing:
      | name   | module       |
      | first  | math.add     |
      | second | math.add     |
      | screen | output       |
    And "first" output "out" is wired to "second" input "a"
    And "second" output "out" is wired to "first" input "a"
    And "second" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports an issue containing "feeds back into itself"

  Scenario: A well-formed patch compiles cleanly
    Given a patch containing:
      | name   | module       |
      | coords | coord        |
      | tint   | color.hsv   |
      | screen | output       |
    And "coords" output "x" is wired to "tint" input "hue"
    And "tint" output "color" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues
    And the program contains at least one "HsvToRgb" op
