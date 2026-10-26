Feature: A value is turned by dragging its well
  An input with no wire rests on a value of its own, shown in a well at the right of
  its row with a level line under it. Dragging the well up or down with the left
  button turns the value, as one step to undo, and Alt-click puts it back to the
  value a new module starts with.

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
