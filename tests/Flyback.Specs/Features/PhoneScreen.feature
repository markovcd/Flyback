Feature: The editor fits a window as narrow as a phone held upright
  At 390 pixels wide, a tablet or a phone held upright, on the desktop as in a page,
  every button is within reach: the toolbar wraps onto more rows, the side button
  shows the preview and the inspector in the canvas's place, and what the editor
  says keeps its room on the status line.

  Background:
    Given the clock on its own
    And a sine beside the clock
    And the screen is a phone held upright

  Scenario: Every toolbar button is on the screen, however many rows that takes
    Given the patch is open in the editor
    When a finger taps bare canvas
    Then every toolbar button is on the screen

  Scenario: The side button shows the inspector in the canvas's place, and the canvas again
    Given the patch is open in the editor
    When a finger taps the sine
    And the side button is pressed in
    Then the canvas has no width
    And the module panel's "delete-modules" button is on the screen
    When the side button is let out
    Then the canvas has the window's width

  Scenario: What the editor says keeps its room on the status line
    Given the patch is open in the editor
    Then the status line leaves the report at least 100 pixels

  Scenario: The empty panel says what a finger does, once one has touched the canvas
    Given the patch is open in the editor
    When a finger taps bare canvas
    And the side button is pressed in
    Then the module panel says "Hold a finger on bare canvas"
    And the module panel does not say "Right-click"

  Scenario: The preset gallery a finger opens leaves the on-screen keyboard down
    Given the patch is open in the editor
    When a finger taps bare canvas
    And the preset gallery is opened
    Then the gallery's filter box waits to be tapped

  Scenario: Unsaved changes in a page are asked about with every answer on the screen, and no Save
    Given the editor is in a page
    And the patch is open in the editor
    When a finger taps the sine
    And the side button is pressed in
    And the module panel's "delete-modules" button is pressed
    And the preset "Plasma" is picked
    Then the question offers "Discard changes, Cancel", each on the screen
