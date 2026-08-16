Feature: Compiling a patch
  Compilation walks back from the Output node and lowers what it reaches.
  A patch is edited live, so every failure has to degrade into something that
  still renders rather than throwing — the editor must survive a half-built
  graph.

  Specified by ADR-0011.

  # Said only because there is no Audio Output either. A sink missing while the
  # other one is present is a choice; both missing is a patch that does nothing.
  Scenario: A patch with no output at all renders black and says so
    Given a patch containing:
      | name | module   |
      | wave | osc.sine |
    When the patch is compiled
    Then compilation reports an issue containing "no output"
    And the rendered image is entirely black

  # An oscillator accumulates how far its 'in' moved, so one left on its knob
  # holds a single value: silence at the speakers, a flat field on the screen.
  # The patch is exactly what was asked for and compiles to something valid,
  # which is why this is said rather than refused.
  Scenario: A module with nothing driving its domain is remarked on, not refused
    Given a patch containing:
      | name   | module       |
      | osc    | osc.sine     |
      | screen | video.output |
    And "osc" output "out" is wired to "screen" input "colour"
    When the patch is compiled
    Then compilation reports an issue containing "never moves"
    And compilation reports nothing wrong

  Scenario: A domain that is driven is not remarked on
    Given a patch containing:
      | name   | module       |
      | clock  | time         |
      | osc    | osc.sine     |
      | screen | video.output |
    And "clock" output "t" is wired to "osc" input "in"
    And "osc" output "out" is wired to "screen" input "colour"
    When the patch is compiled
    Then compilation reports no issues

  Scenario: An unknown module is reported rather than throwing
    Given a patch containing:
      | name   | module       |
      | screen | video.output |
    And a node named "mystery" of unknown type "module.from.the.future"
    And "mystery" output 0 is wired to "screen" input "colour"
    When the patch is compiled
    Then compilation reports an issue containing "Unknown module"
    And the rendered image is entirely black

  Scenario: A cycle is reported instead of hanging
    Given a patch containing:
      | name   | module       |
      | first  | math.add     |
      | second | math.add     |
      | screen | video.output |
    And "first" output "out" is wired to "second" input "a"
    And "second" output "out" is wired to "first" input "a"
    And "second" output "out" is wired to "screen" input "colour"
    When the patch is compiled
    Then compilation reports an issue containing "feeds back into itself"

  Scenario: A well-formed patch compiles cleanly
    Given a patch containing:
      | name   | module       |
      | coords | coord        |
      | tint   | colour.hsv   |
      | screen | video.output |
    And "coords" output "x" is wired to "tint" input "hue"
    And "tint" output "colour" is wired to "screen" input "colour"
    When the patch is compiled
    Then compilation reports no issues
    And the program contains at least one "HsvToRgb" op
