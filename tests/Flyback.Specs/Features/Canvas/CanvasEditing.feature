Feature: What is done on the canvas is one edit, and changes only what was asked
  Deleting, switching off, grouping, laying out and duplicating each change the
  patch as one edit, however many modules they touch: one undo takes all of it
  back. Grouping and laying out change how the patch is drawn, never what it plays.

  Specified by ADR-0044, ADR-0092 and ADR-0117.

  Background:
    Given a level of 0.3 shown through a module that halves it
    And the patch is open in the editor

  Scenario: Deleting several modules comes back with one undo
    Given the level and the halving module are selected
    When the selection is deleted
    Then the screen is black
    When that is undone
    Then the screen shows 0.15

  Scenario: The same key switches a module off and back on
    Given the halving module is selected
    When the selection is switched off
    Then the screen shows 0.3
    When the selection is switched on again
    Then the screen shows 0.15

  Scenario: Grouping and laying out change nothing the patch plays
    Given the level and the halving module are selected
    When the selection is grouped
    And the patch is laid out
    Then the level and the halving module are drawn as one box
    And the screen shows 0.15

  Scenario: A duplicate arrives selected, and one undo takes it away
    Given the halving module is selected
    When the selection is duplicated
    Then there are two halving modules, and the new one is selected
    When that is undone
    Then there is one halving module
