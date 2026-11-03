Feature: A tap on the picture leaves the patch playing
  A click or a finger on the picture does nothing to the patch, in the editor and the
  desktop viewer alike, so a stray click while playing live never pauses it: the pause
  button and Space pause. A double-click gives the picture the whole screen, and leaves
  the patch playing or paused as it was.

  Background:
    Given a rainbow across the screen

  Scenario: A tap on the editor's picture leaves the patch playing
    Given the patch is open in the editor
    And the patch is playing
    When the picture is tapped
    Then the patch is playing

  Scenario: A double-click on the editor's picture gives it the whole window and leaves the patch playing
    Given the patch is open in the editor
    And the patch is playing
    When the picture is given the whole window
    Then the patch is playing

  Scenario: A tap on the editor's full-screen picture leaves the patch playing
    Given the patch is open in the editor
    And the patch is playing
    When the picture is given the whole window
    And the picture is tapped
    Then the patch is playing
    And the picture has the whole window

  Scenario: A tap on the viewer's picture leaves the patch playing
    When the viewer plays it
    And the viewer's picture is tapped
    Then the viewer is playing

  Scenario: A double-click on the viewer's picture gives it the whole screen and leaves the patch playing
    When the viewer plays it
    And the viewer's picture is double-clicked
    Then the viewer has the whole screen
    And the viewer is playing
