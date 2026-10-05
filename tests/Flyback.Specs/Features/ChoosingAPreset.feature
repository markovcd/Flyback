Feature: A preset is chosen in the gallery before it is opened
  The gallery lists every preset as a card, with what narrows the list on one
  side and the chosen card described on the other. A click chooses a card and
  leaves the canvas alone; the gallery's button, Enter or a double-click opens it.

  Scenario: Choosing a card describes it and keeps the patch on the canvas
    Given a sine driven by Time
    When the preset gallery is opened
    And the "Kaleidoscope" card is chosen
    Then the gallery describes "Kaleidoscope"
    And the canvas still holds the sine
    When the chosen card is used
    Then the preset on the canvas is "Kaleidoscope"

  Scenario: The gallery narrows to the presets that are heard
    When the preset gallery is opened
    And the gallery's "Sound" row is pressed
    Then the gallery shows a card for every preset that is heard, and no other
