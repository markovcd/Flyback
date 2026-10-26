Feature: Linking a value to a panel knob
  A clicked well takes Alt+L, which drops the panel's knobs below it with a new one
  last. Picking one hands the value to that knob: the socket becomes a filled diamond
  and a chip naming the knob stands in the well. A click on the chip drops the list
  again, where Unlink lets the value go at the number the knob gave it.

  Background:
    Given a sine on its own

  Scenario: Alt+L links a clicked well to a new panel knob
    When the sine's freq well is clicked
    And Alt+L is pressed
    Then the list of knobs reads "New knob"
    When "New knob" is picked from the list of knobs
    Then the sine's freq follows "Knob 1"

  Scenario: The chip of a linked value unlinks it and keeps its value
    Given the sine's freq is linked to a new panel knob
    When the sine's freq well is clicked
    And "Unlink" is picked from the list of knobs
    Then the sine's freq follows no knob
    And the sine's freq is where the knob had it
