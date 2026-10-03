Feature: A tap on the picture plays and pauses it
  A click or a finger on the picture pauses the patch and, tapped again, plays it, in the
  editor and the viewer alike. A double-click still gives the picture the whole screen,
  and leaves the patch playing or paused as it was.

  Background:
    Given a rainbow across the screen

  Scenario: A tap on the editor's picture pauses the patch, and another plays it
    Given the patch is open in the editor
    And the patch is playing
    When the picture is tapped
    Then the patch is paused
    When the picture is tapped
    Then the patch is playing

  Scenario: A double-click on the editor's picture gives it the whole window and leaves the patch playing
    Given the patch is open in the editor
    And the patch is playing
    When the picture is given the whole window
    Then the patch is playing

  Scenario: A tap on the viewer's picture pauses the patch, and another plays it
    When the viewer plays it
    And the viewer's picture is tapped
    Then the viewer is paused
    When the viewer's picture is tapped
    Then the viewer is playing

  Scenario: A double-click on the viewer's picture gives it the whole screen and leaves the patch playing
    When the viewer plays it
    And the viewer's picture is double-clicked
    Then the viewer has the whole screen
    And the viewer is playing
