Feature: Work nobody saved is asked about before anything replaces it
  Picking a preset, opening a file or closing the window over changes nobody saved
  asks first, and Cancel leaves the work exactly as it was.

  Specified by ADR-0071.

  Background:
    Given a level of 0.3 shown through a module that halves it
    And the patch is open in the editor
    And the level is deleted

  Scenario: Picking a preset over unsaved changes asks first
    When the preset "Plasma" is picked
    Then the editor asks about unsaved changes

  Scenario: Cancel keeps the work
    When the preset "Plasma" is picked
    And the question is answered "Cancel"
    Then the patch has unsaved changes
    And the screen is black

  Scenario: Discarding the changes lets the preset in
    When the preset "Plasma" is picked
    And the question is answered "Discard changes"
    Then the editor shows the preset "Plasma"
    And the patch has no unsaved changes
