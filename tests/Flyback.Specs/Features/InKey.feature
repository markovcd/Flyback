Feature: A pitch can be kept in key
  A pitch that wanders between notes snaps to the nearest note the scale has
  switched on, so a sweep becomes a run up the scale.

  Specified by ADR-0051.

  Scenario: A pitch between notes lands on the nearest note of the scale
    Given a pitch of 61.4 kept to C major
    Then the note that comes out is 62

  Scenario: With every note switched on a pitch snaps to the nearest semitone
    Given a pitch of 61.4 kept to all twelve notes
    Then the note that comes out is 61
