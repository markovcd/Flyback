Feature: A value is turned by dragging its well
  An input with no wire rests on a value of its own, shown in a well at the right of
  its row with a level line under it. Dragging the well up or down with the left
  button turns the value, as one step to undo, with a card over the module reading
  it out while it turns. Alt-click puts it back to the value a new module starts with.
  A click gives the well the keys: the arrows nudge it, and Enter opens it for an
  exact value, which no bare key does, so the keyboard still plays notes.

  Background:
    Given a sine on its own
    And the patch is open in the editor

  Scenario: Dragging the well up raises the value, and one undo takes it back
    When the sine's freq well is dragged up
    Then the sine's freq is higher than it was
    When that is undone
    Then the sine's freq is back where it was

  Scenario: Alt-click puts the value back to its default
    Given the sine's freq is turned away from its default
    When the sine's freq well is Alt-clicked
    Then the sine's freq is back at its default

  Scenario: A card reads the value out while it turns, and goes when it is let go
    When the sine's freq well is held and dragged up
    Then a card over the canvas reads out the sine's freq
    When the well is let go
    Then the card is gone

  Scenario: A clicked well takes an exact value typed after Enter
    When the sine's freq well is clicked
    And Enter is pressed
    And "0.9" is typed
    And Enter is pressed
    Then the sine's freq is 0.9

  Scenario: Up nudges a clicked well
    When the sine's freq well is clicked
    And Up is pressed
    Then the sine's freq is higher than it was

  Scenario: A finger taps a well and steps it up with the button under the module
    When a finger taps the sine's freq well
    And a finger taps "+ step" under the sine
    Then the sine's freq is higher than it was
