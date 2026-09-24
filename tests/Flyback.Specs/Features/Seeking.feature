Feature: The patch's clock can be moved anywhere along the seek bar
  A piece that changes over minutes is worked on at the minute being worked on:
  the seek bar takes the picture and the sound there, rather than waiting for it
  to come round.

  Scenario: Clicking along the seek bar moves the patch's clock there
    Given a rainbow across the screen
    And the patch is open in the editor
    When the seek bar is clicked at 30 seconds
    Then the patch's clock is at about 30 seconds

  Scenario: A paused patch moved along the seek bar stays paused there
    Given a rainbow across the screen
    And the patch is open in the editor
    And the patch is paused
    When the seek bar is clicked at 45 seconds
    Then the patch is still paused
    And the patch's clock is at about 45 seconds

  Scenario: The seek bar spans the length typed beside it
    Given the patch is open in the editor
    When the seek bar's length is set to "2:30"
    Then the seek bar reaches 150 seconds

  Scenario: A looped patch comes round to the start at the end of the seek bar
    Given a rainbow across the screen
    And the patch is open in the editor
    And the seek bar loops
    When the patch plays on past the end of the seek bar
    Then the patch's clock is at about 0 seconds
