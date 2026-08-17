Feature: Inputs carry their own value until something is patched in
  Most inputs in a real patch are constants, so every input is a knob on the
  node rather than a separate module wired in. Patching one overrides the knob;
  unplugging brings it back, because it was never lost.

  Specified by ADR-0009 and ADR-0020.

  Scenario: An unwired input compiles to the value on the node
    Given a patch containing:
      | name   | module       |
      | knob   | value        |
      | screen | output       |
    And "knob" input "value" is set to 0.25
    And "knob" output "out" is wired to "screen" input "colour"
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
    And "sum" output "out" is wired to "screen" input "colour"
    When the patch is compiled
    Then the centre pixel is about 0.2, 0.2, 0.2
    And "sum" input "a" still holds 0.9

  # A patch saved before a module gained inputs still opens: the missing
  # trailing values fall back to the module's own defaults, so amp resolves to
  # 1 rather than to 0, which would have rendered black.
  Scenario: A patch saved without the newer inputs falls back to the defaults
    Given a patch containing:
      | name   | module       |
      | osc    | osc.sine     |
      | screen | output       |
    And "osc" input "in" is set to 0.25
    And "osc" input "freq" is set to 1
    And "osc" has only 2 stored input values
    And "osc" output "out" is wired to "screen" input "colour"
    When the patch is compiled
    # Nothing wrong rather than nothing said: 'in' is deliberately on its knob
    # here, which is a still picture and is remarked on for that reason.
    Then compilation reports nothing wrong
    And the centre pixel is about 1, 1, 1
