Feature: A patch in any state still plays
  A patch is edited live, so every state it passes through on the way to a
  finished one has to show and sound like something. What is wrong is said, not
  thrown.

  Specified by ADR-0011, and by ADR-0050 for sockets that carry a signal before
  anything is patched into them.

  # Only a patch assembled by hand can lack its Output; the editor, presets and
  # files always carry one.
  Scenario: A patch with no Output at all is black and says so
    Given a sine with no Output to reach
    Then Flyback says the patch has no Output
    And the screen is black

  # A flat color and silence look exactly like a patch that is working, so the
  # one thing worth saying is said.
  Scenario: An Output with nothing patched into it is pointed out
    Given an Output with nothing patched into it
    Then Flyback points out that nothing reaches the Output
    And the speakers are silent

  Scenario: An Output with something patched into it is not remarked on
    Given a level of 0.5 on the screen
    Then the patch is accepted without complaint

  Scenario: A module from a newer Flyback is reported rather than crashing
    Given a module from a newer Flyback patched to the screen
    Then Flyback reports an unknown module
    And the screen is black

  Scenario: A finished patch is accepted without complaint
    Given a rainbow across the screen
    Then the patch is accepted without complaint
    And the screen is not black

  # An oscillator's domain is normalled to Time, so it runs with nothing plugged
  # in and no Time module anywhere in the patch.
  Scenario: An oscillator with nothing patched in runs on the clock
    Given a sine on the screen with nothing patched into it
    Then the patch is accepted without complaint
    And the patch reads the clock

  # The knob is not what a normalled socket follows. Left at a quarter cycle the
  # sine would be a flat white field; on the clock it starts at nothing.
  Scenario: An oscillator with nothing patched in ignores the knob on its domain
    Given a sine on the screen with its unpatched domain knob at a quarter cycle
    Then the screen shows 0

  Scenario: Patching into an oscillator's domain takes it off the clock
    Given a sine on the screen driven by the horizontal position
    Then the patch is accepted without complaint
    And the patch does not read the clock

  Scenario: Oscillators left on the clock share one reading of it
    Given two oscillators mixed on the screen with nothing patched into either
    Then the patch reads the clock once

  Scenario: An oscillator driven by Time is not remarked on
    Given a sine on the screen driven by Time
    Then the patch is accepted without complaint
