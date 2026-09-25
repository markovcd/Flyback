Feature: The patch's clock can be moved anywhere along the seek bar
  A piece that changes over minutes is worked on at the minute being worked on:
  the seek bar takes the picture and the sound there, rather than waiting for it
  to come round. It spans the patch's own length, and the patch stops at its end
  unless the bar loops.

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

  Scenario: The seek bar spans three minutes for a patch that says nothing about its length
    Given the patch is open in the editor
    Then the seek bar reaches 180 seconds

  Scenario: The length typed beside the seek bar is the patch's, to a hundredth of a second
    Given the patch is open in the editor
    When the seek bar's length is set to "2:30.25"
    Then the seek bar reaches 150.25 seconds
    And the patch plays for 150.25 seconds

  Scenario: An unlooped patch stops at the end of its length
    Given a rainbow across the screen
    And the patch is open in the editor
    When the patch plays to the end of the seek bar
    Then the patch has stopped
    And the patch's clock is at about 180 seconds

  Scenario: A looped patch comes round to the start at the end of the seek bar
    Given a rainbow across the screen
    And the patch is open in the editor
    And the seek bar loops
    When the patch plays on past the end of the seek bar
    Then the patch's clock is at about 0 seconds
