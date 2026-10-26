Feature: An input a formula does not read is drawn faint
  An Expression has four inputs, but its formula may read only some of them.
  One it never reads is drawn faint and has no well, since nothing set on it
  does anything; it comes back to full strength once the formula reads it.

  Scenario: An Expression's unread inputs are faint and have no well
    Given an Expression whose formula is "a * 0.11"
    And the patch is open in the editor
    Then the Expression's "b" is drawn faint with no well
    And the Expression's "a" is drawn in full with a well
