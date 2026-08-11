Feature: Compiling a patch
  Compilation walks back from the Output node and lowers what it reaches.
  A patch is edited live, so every failure has to degrade into something that
  still renders rather than throwing — the editor must survive a half-built
  graph.

  Specified by ADR-0011.

  Scenario: A patch with no Output node renders black and says so
    Given a patch containing:
      | name | module   |
      | wave | osc.sine |
    When the patch is compiled
    Then compilation reports an issue containing "No Output node"
    And the rendered image is entirely black

  Scenario: An unknown module is reported rather than throwing
    Given a patch containing:
      | name   | module |
      | screen | output |
    And a node named "mystery" of unknown type "module.from.the.future"
    And "mystery" output 0 is wired to "screen" input "colour"
    When the patch is compiled
    Then compilation reports an issue containing "Unknown module"
    And the rendered image is entirely black

  Scenario: A cycle is reported instead of hanging
    Given a patch containing:
      | name   | module   |
      | first  | math.add |
      | second | math.add |
      | screen | output   |
    And "first" output "out" is wired to "second" input "a"
    And "second" output "out" is wired to "first" input "a"
    And "second" output "out" is wired to "screen" input "colour"
    When the patch is compiled
    Then compilation reports an issue containing "feeds back into itself"

  Scenario: A well-formed patch compiles cleanly
    Given a patch containing:
      | name   | module     |
      | coords | coord      |
      | tint   | colour.hsv |
      | screen | output     |
    And "coords" output "x" is wired to "tint" input "hue"
    And "tint" output "colour" is wired to "screen" input "colour"
    When the patch is compiled
    Then compilation reports no issues
    And the program contains at least one "HsvToRgb" op
