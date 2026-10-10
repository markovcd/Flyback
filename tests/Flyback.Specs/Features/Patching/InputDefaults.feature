Feature: Every input is a knob until something is patched into it
  Most inputs in a real patch are constants, so every input carries its own
  value on the module. Patching one overrides the knob; unplugging brings it
  back, because it was never lost.

  Specified by ADR-0009 and ADR-0020, and by ADR-0050 for the inputs that have
  no knob to carry.

  Scenario: An unpatched input uses its knob
    Given a level of 0.25 on the screen
    Then the screen shows 0.25

  Scenario: Patching into an input overrides its knob without losing it
    Given an adder whose knob reads 0.9, with a level of 0.2 patched in over it
    Then the screen shows 0.2
    And the adder's knob still reads 0.9

  # Amplitude falls back to 1 rather than 0, which would have been black.
  Scenario: A patch saved before a module gained knobs opens with their defaults
    Given a sine held at a quarter cycle, saved before it had amplitude and offset knobs
    Then the patch is accepted without complaint
    And the screen shows 1
