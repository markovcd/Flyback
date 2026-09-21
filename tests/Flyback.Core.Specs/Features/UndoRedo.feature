Feature: Every edit can be undone
  An edit can be taken back and put back again, and a patch undone to what was
  opened has nothing left to save.

  Specified by ADR-0071.

  Background:
    Given a level of 0.25 on the screen
    And the patch has just been opened

  Scenario: A freshly opened patch has nothing to undo
    Then there is nothing to undo

  Scenario: Undoing a deletion brings the module back with its wire
    When the level is deleted
    Then the screen is black
    When that is undone
    Then the screen shows 0.25

  Scenario: Redoing puts the edit back
    When the level is deleted
    And that is undone
    And it is redone
    Then the screen is black

  Scenario: Undoing back to what was opened leaves nothing to save
    When the level is deleted
    Then the patch has unsaved changes
    When that is undone
    Then the patch has no unsaved changes
