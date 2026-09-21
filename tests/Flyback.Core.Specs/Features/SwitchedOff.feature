Feature: A module switched off is a wire
  Switching a module off takes it out of the signal path without unpatching it.
  Whatever is patched into it arrives where it would have gone, and where
  nothing is patched in, the socket downstream reads its own knob.

  Specified by ADR-0117.

  Scenario: A module that is on does its work
    Given a patch containing:
      | name   | module   |
      | level  | value    |
      | half   | math.mul |
      | screen | output   |
    And "level" input "value" is set to 0.3
    And "half" input "b" is set to 0.5
    And "level" output "out" is wired to "half" input "a"
    And "half" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then the centre pixel is about 0.15, 0.15, 0.15

  Scenario: A module that is off hands on what is patched into it
    Given a patch containing:
      | name   | module   |
      | level  | value    |
      | half   | math.mul |
      | screen | output   |
    And "level" input "value" is set to 0.3
    And "half" input "b" is set to 0.5
    And "level" output "out" is wired to "half" input "a"
    And "half" output "out" is wired to "screen" input "color"
    And "half" is switched off
    When the patch is compiled
    Then compilation reports no issues
    And the centre pixel is about 0.3, 0.3, 0.3

  Scenario: A chain of modules that are off is followed to its far end
    Given a patch containing:
      | name   | module   |
      | level  | value    |
      | half   | math.mul |
      | twice  | math.mul |
      | screen | output   |
    And "level" input "value" is set to 0.3
    And "half" input "b" is set to 0.5
    And "twice" input "b" is set to 2
    And "level" output "out" is wired to "half" input "a"
    And "half" output "out" is wired to "twice" input "a"
    And "twice" output "out" is wired to "screen" input "color"
    And "half" is switched off
    And "twice" is switched off
    When the patch is compiled
    Then the centre pixel is about 0.3, 0.3, 0.3

  Scenario: With nothing patched in, the socket downstream reads its own knob
    Given a patch containing:
      | name   | module   |
      | half   | math.mul |
      | sum    | math.add |
      | screen | output   |
    And "sum" input "a" is set to 0.1
    And "sum" input "b" is set to 0.1
    And "half" output "out" is wired to "sum" input "a"
    And "sum" output "out" is wired to "screen" input "color"
    And "half" is switched off
    When the patch is compiled
    Then the centre pixel is about 0.2, 0.2, 0.2

  Scenario: A module that is off costs nothing
    Given a patch containing:
      | name   | module   |
      | coords | coord    |
      | half   | math.mul |
      | screen | output   |
    And "coords" output "x" is wired to "half" input "a"
    And "half" output "out" is wired to "screen" input "color"
    And "half" is switched off
    When the patch is compiled
    Then the program contains no "Mul" ops
