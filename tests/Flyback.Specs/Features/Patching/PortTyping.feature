Feature: Single values and colors share the same wires
  A patch mixes single values and colors freely, and any wire takes either. A
  patch of single values never pays for three-wide arithmetic.

  Specified by ADR-0007 and ADR-0010.

  Scenario: A single value on a color input is a gray
    Given a level of 0.5 on the screen
    Then the screen shows 0.5, 0.5, 0.5

  # Rec. 709 luma, the video-correct narrowing, not a plain average.
  Scenario: A color on a single-value input becomes its brightness
    Given pure red patched into an input that takes a single value
    Then the screen shows 0.2126

  Scenario: One Multiply scales a color by a single value
    Given the color 1, 0.5, 0 halved by a Multiply
    Then the patch is accepted without complaint
    And the screen shows 0.5, 0.25, 0

  Scenario: Splitting a color recovers its channels
    Given the color 0.75, 0.25, 1 split, with its green on the screen
    Then the screen shows 0.25
