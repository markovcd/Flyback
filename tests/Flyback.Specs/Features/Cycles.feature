Feature: A patch may be wired back into itself
  A loop is a patch, not a mistake. What comes back round is what the loop made
  a moment before: the previous sample at the speakers, the previous frame at the
  same spot on the screen.

  Specified by ADR-0075, resting on ADR-0074.

  Background:
    Given a loop that halves what it made last and adds a quarter

  Scenario: A loop is accepted without complaint
    Given the loop is shown on the screen
    Then the patch is accepted without complaint

  Scenario: At the speakers a loop remembers the sample before
    Given the loop is heard at the speakers
    Then each sample builds on the last: 0.25, 0.375, 0.4375, 0.46875

  Scenario: On the screen a loop remembers the frame before
    Given the loop is shown on the screen
    Then each frame builds on the last: 0.25, 0.375, 0.4375

  Scenario: Rewinding makes a loop forget
    Given the loop is shown on the screen
    Then after 5 frames and a rewind the next frame is back at 0.25
