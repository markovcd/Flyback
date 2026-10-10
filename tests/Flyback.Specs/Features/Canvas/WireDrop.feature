Feature: A wire let go over bare canvas is plugged into what is picked for it
  Letting a wire go where there is no socket opens the list of modules, and the one
  picked from it arrives with the wire already in it.

  Specified by ADR-0046.

  Scenario: A module picked for a dropped wire arrives wired
    Given the clock on its own
    And the patch is open in the editor
    When a wire from the clock is dropped on bare canvas
    And "Sine" is picked from the list that opens
    Then a sine is fed by the clock
