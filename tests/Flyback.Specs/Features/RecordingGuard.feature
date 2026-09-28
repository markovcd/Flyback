Feature: A recording keeps the editor on the patch being captured
  A take belongs to the patch it started on, so the editor cannot replace that
  patch or close its window before the recording has stopped.

  Scenario: Opening controls and the window are unavailable during a take
    Given a recording is in progress
    Then the Open and preset controls are disabled
    When someone tries to close the editor
    Then the editor remains open
