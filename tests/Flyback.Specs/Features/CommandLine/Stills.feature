@ffmpeg
Feature: A build draws every preset's still once, for every program to show
  flyback-cli stills draws a still of each preset the build offers and writes the index
  the galleries read them by, so the editor, the web editor and the presets page show a
  shipped preset's picture without drawing it.

  Scenario: Every preset is listed and every picture has its still
    When flyback-cli draws the stills
    Then the command succeeds
    And the index lists every preset in the editor's order, under its heading, each picture with its still
    And the index gives every preset the tags the presets page filters by
