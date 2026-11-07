Feature: The canvas can have the preview's column too
  The toolbar's side button puts the preview and the inspector away and gives their
  width to the canvas, and brings them back at the width they had. While the canvas
  is swapped into that column, putting it away gives its width to the picture.

  Scenario: Putting the side column away widens the canvas until it is brought back
    Given a rainbow across the screen
    And the patch is open in the editor
    When the side column is put away
    Then the canvas has the preview's width as well as its own
    When the side column is brought back
    Then the canvas is as wide as it was

  Scenario: Putting the side column away while swapped widens the picture
    Given a rainbow across the screen
    And the patch is open in the editor
    When the preview and the canvas are swapped
    Then the side column is showing with the canvas in it
    When the side column is put away again
    Then the picture has the canvas's width as well as its own

  Scenario: Swapping leaves a side column that was put away where it is
    Given a rainbow across the screen
    And the patch is open in the editor
    When the side column is put away
    And the preview and the canvas are swapped
    Then the side column is still away
