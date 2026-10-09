Feature: A picture that has the screen answers the same keys everywhere
  Somebody showing a patch reaches for the keys they learned on the last picture:
  Space or Ctrl+P plays and pauses, F3 says how it draws, and Escape or F11 gives the
  screen back, over the editor's full-screen picture and the viewer's alike.

  Scenario: Space pauses the editor's full-screen picture
    Given a rainbow across the screen
    And the patch is open in the editor
    And the patch is playing
    When the picture is given the whole window
    And Space is pressed over the picture
    Then the patch is paused

  Scenario: F11 gives the editor its window back
    Given a rainbow across the screen
    And the patch is open in the editor
    When the picture is given the whole window
    And F11 is pressed over the picture
    Then the picture does not have the whole window
