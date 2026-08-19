Feature: Scalars and colors travel down the same wires
  A patch mixes single signals and three-channel colors freely. Widths are
  resolved when the patch compiles, so the interpreter never learns that colors
  exist and a scalar-only patch never pays for three-wide arithmetic.

  Specified by ADR-0007 and ADR-0010.

  Scenario: A scalar entering a color port broadcasts to all three channels
    Given a patch containing:
      | name   | module       |
      | knob   | value        |
      | screen | output       |
    And "knob" input "value" is set to 0.5
    And "knob" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then the centre pixel is about 0.5, 0.5, 0.5

  # Rec. 709 luma, not a plain average — the video-correct narrowing.
  Scenario: A color entering a scalar port narrows to its luma
    Given a patch containing:
      | name   | module       |
      | red    | color.rgb   |
      | turn   | space.rotate |
      | screen | output       |
    And "red" input "r" is set to 1
    And "red" input "g" is set to 0
    And "red" input "b" is set to 0
    And "red" output "color" is wired to "turn" input "x"
    And "turn" output "x" is wired to "screen" input "color"
    When the patch is compiled
    Then the centre pixel is about 0.2126, 0.2126, 0.2126

  Scenario: One Multiply handles a color scaled by a scalar
    Given a patch containing:
      | name   | module       |
      | tint   | color.rgb   |
      | half   | math.mul     |
      | screen | output       |
    And "tint" input "r" is set to 1
    And "tint" input "g" is set to 0.5
    And "tint" input "b" is set to 0
    And "tint" output "color" is wired to "half" input "a"
    And "half" input "b" is set to 0.5
    And "half" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues
    And the centre pixel is about 0.5, 0.25, 0

  Scenario: Splitting a color recovers its channels
    Given a patch containing:
      | name    | module       |
      | tint    | color.rgb   |
      | channel | color.split |
      | screen  | output       |
    And "tint" input "r" is set to 0.75
    And "tint" input "g" is set to 0.25
    And "tint" input "b" is set to 1
    And "tint" output "color" is wired to "channel" input "color"
    And "channel" output "g" is wired to "screen" input "color"
    When the patch is compiled
    Then the centre pixel is about 0.25, 0.25, 0.25
