Feature: Only what the Output can reach is compiled
  Modules get dropped on the canvas before being wired, and half-built ideas get
  left lying around. None of that should cost anything per pixel.

  Specified by ADR-0011.

  Scenario: A module the Output cannot reach emits no ops
    Given a patch containing:
      | name    | module        |
      | coords  | coord         |
      | rubbish | pattern.noise |
      | screen  | output        |
    And "coords" output "x" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues
    And the program contains no "Noise3" ops

  # Elimination is per module, not per port: reaching Coordinates at all emits
  # everything it computes, including the radius and angle nobody asked for.
  Scenario: A module feeding several inputs is still emitted once
    Given a patch containing:
      | name   | module       |
      | coords | coord        |
      | tint   | color.hsv   |
      | screen | output       |
    And "coords" output "x" is wired to "tint" input "hue"
    And "coords" output "x" is wired to "tint" input "saturation"
    And "coords" output "x" is wired to "tint" input "value"
    And "tint" output "color" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues
    And the program contains exactly 1 "LoadX" op
