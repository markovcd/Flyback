Feature: Warehouse is a whole track
  The Easy plugin's Warehouse is acid house as a song rather than a loop: ninety-six
  bars at 124 bpm, with an intro, builds, two drops, a breakdown and a way out that
  leads back into the start.

  Scenario: Warehouse plays ninety-six bars before it comes round
    Given the shipped preset "Warehouse"
    Then it opens with nothing wrong
    And it plays for 185.81 seconds before it comes round
