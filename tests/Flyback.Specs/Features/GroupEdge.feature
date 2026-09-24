Feature: Any unwired socket inside a box can go on its edge
  A box's edge starts as the sockets wires cross, and a socket nothing is
  plugged into can be put there too, so the box is ready for the wire it will get.

  Scenario: A socket nothing is plugged into goes on the box's edge
    Given a sine driven by Time
    And the sine is drawn in one box with a Multiply it feeds
    When the Multiply's "b" is put on the box's edge
    Then the box has a "Multiply.b" socket

  Scenario: A socket with a wire on it is not offered for the edge
    Given a sine driven by Time
    And the sine is drawn in one box with a Multiply it feeds
    Then the Multiply's "a" cannot be put on the box's edge
