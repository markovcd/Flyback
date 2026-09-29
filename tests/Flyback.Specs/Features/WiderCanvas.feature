Feature: The canvas can have the preview's column too
  The toolbar's side button puts the preview and the inspector away and gives their
  width to the canvas, and brings them back at the width they had. While the canvas
  is swapped into that column, the column stays.

  Scenario: Putting the side column away widens the canvas until it is brought back
    Given a rainbow across the screen
    And the patch is open in the editor
    When the side column is put away
    Then the canvas has the preview's width as well as its own
    When the side column is brought back
    Then the canvas is as wide as it was

  Scenario: The side column stays while the canvas is swapped into it
    Given a rainbow across the screen
    And the patch is open in the editor
    When the side column is put away
    And the preview and the canvas are swapped
    Then the side column is showing and cannot be put away
