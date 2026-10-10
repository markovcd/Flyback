Feature: The empty panel lists what the canvas can do in groups that fold
  With nothing selected the inspector lists every gesture the canvas has, a
  group at a time, so the one wanted is found by its heading rather than by
  reading a wall of text. The first group is open and the rest wait.

  Background:
    Given the clock on its own
    And the patch is open in the editor

  Scenario: Getting started is open and the other groups are folded
    Then the panel's "Getting started" group is open
    And the panel's "Patching" group is folded

  Scenario: A group opens when its heading is pressed, and folds when it is pressed again
    When the panel's "Patching" group is pressed
    Then the panel's "Patching" group is open
    And the panel's "Getting started" group is open
    When the panel's "Patching" group is pressed
    Then the panel's "Patching" group is folded
