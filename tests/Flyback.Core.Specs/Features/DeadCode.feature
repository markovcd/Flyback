Feature: Only what reaches the Output costs anything
  Modules get dropped on the canvas before being wired, and half-built ideas get
  left lying around. None of that should cost anything per pixel.

  Specified by ADR-0011.

  Scenario: A module left unwired costs nothing
    Given noise left unwired beside a picture on the screen
    Then the patch is accepted without complaint
    And the unwired noise costs nothing

  # Per module, not per port: reaching Coordinates at all computes everything it
  # offers, the radius and angle included.
  Scenario: A module feeding several inputs is worked out once
    Given the horizontal position feeding all three inputs of a color
    Then the patch is accepted without complaint
    And the position is worked out once
