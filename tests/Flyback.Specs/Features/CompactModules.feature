Feature: Compact modules
  With compact modules switched on in the canvas settings, each input sits beside
  an output on one row, so a module takes less of the canvas. What an unwired
  input is set to moves off the row and into the tip shown when it is hovered.

  Scenario: A compact module is shorter, with inputs beside outputs
    Given a Filter
    When modules are drawn compact
    Then the Filter is shorter than when drawn in full
    And the Filter's first input is level with its first output

  Scenario: Hovering an unwired input says what it is set to
    Given a Filter whose cutoff is set to 1200
    When modules are drawn compact
    Then hovering the Filter's cutoff shows 1200 before what the socket is for
