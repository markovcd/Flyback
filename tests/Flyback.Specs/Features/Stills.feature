Feature: A build draws every preset's still once, for every program to show
  flyback-cli stills draws a still of each preset the build offers and writes the index
  the galleries read them by, so the editor, the web editor and the web viewer show a
  shipped preset's picture without drawing it.

  Scenario: Every preset is listed and every picture has its still
    When flyback-cli draws the stills
    Then the command succeeds
    And the index lists every preset, each picture with its still
