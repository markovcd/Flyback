Feature: Inputs carry their own value until something is patched in
  Most inputs in a real patch are constants, so every input is a knob on the
  node rather than a separate module wired in. Patching one overrides the knob;
  unplugging brings it back, because it was never lost.

  Specified by ADR-0009 and ADR-0020 — and by ADR-0050 for the one kind of
  input that has no knob to carry.

  Scenario: An unwired input compiles to the value on the node
    Given a patch containing:
      | name   | module       |
      | knob   | value        |
      | screen | output       |
    And "knob" input "value" is set to 0.25
    And "knob" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then the centre pixel is about 0.25, 0.25, 0.25

  Scenario: A wire overrides the knob without erasing it
    Given a patch containing:
      | name   | module       |
      | knob   | value        |
      | sum    | math.add     |
      | screen | output       |
    And "sum" input "a" is set to 0.9
    And "sum" input "b" is set to 0
    And "knob" input "value" is set to 0.2
    And "knob" output "out" is wired to "sum" input "a"
    And "sum" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then the centre pixel is about 0.2, 0.2, 0.2
    And "sum" input "a" still holds 0.9

  # A patch saved before a module gained inputs still opens: the missing
  # trailing values fall back to the module's own defaults, so amp resolves to
  # 1 rather than to 0, which would have rendered black.
  Scenario: A patch saved without the newer inputs falls back to the defaults
    Given a patch containing:
      | name   | module       |
      | knob   | value        |
      | osc    | osc.sine     |
      | screen | output       |
    # A quarter of a cycle, wired in rather than left on the socket: 'in' is
    # normalled to Time, and a normalled socket does not read the value stored
    # against it. What is on trial here is 'amp' and 'bias', which are not
    # stored at all.
    And "knob" input "value" is set to 0.25
    And "osc" input "freq" is set to 1
    And "osc" has only 2 stored input values
    And "knob" output "out" is wired to "osc" input "in"
    And "osc" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues
    And the centre pixel is about 1, 1, 1
