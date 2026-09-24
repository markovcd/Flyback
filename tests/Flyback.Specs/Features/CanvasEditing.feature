Feature: What is done on the canvas is one edit, and changes only what was asked
  Deleting, switching off, grouping, laying out and duplicating each change the
  patch as one edit, however many modules they touch: one undo takes all of it
  back. Nothing that only changes how the patch is drawn changes what it plays.

  Specified by ADR-0044, ADR-0092 and ADR-0117.

  Background:
    Given a level of 0.3 shown through a module that halves it
    And the patch is open on the canvas

  Scenario: Deleting several modules comes back with one undo
    When the level and the halving module are selected and deleted
    Then the screen is black
    When the canvas undoes once
    Then the screen shows 0.15

  Scenario: The Output survives deleting everything
    When everything on the canvas is selected and deleted
    Then the patch still has one Output
    And the Output is still selected

  Scenario: One key switches the selection off, and the same key switches it back on
    When the halving module is selected and switched off
    Then the screen shows 0.3
    When the selection is switched again
    Then the screen shows 0.15

  Scenario: Grouping and laying out leave what the patch plays alone
    When the level and the halving module are grouped
    And the patch is laid out
    Then the level and the halving module are one group
    And the screen shows 0.15

  Scenario: A group of one module is refused, and says why
    When the halving module alone is grouped
    Then the patch has no groups
    And the canvas says "A group needs 2 modules or more"

  Scenario: A duplicate arrives selected, and one undo takes it away
    When the halving module is selected and duplicated
    Then there are two halving modules, and only the new one is selected
    When the canvas undoes once
    Then there is one halving module

  Scenario: Undoing on the canvas back to the opened patch leaves nothing to save
    When the level and the halving module are selected and deleted
    Then the canvas has unsaved changes
    When the canvas undoes once
    Then the canvas has no unsaved changes
