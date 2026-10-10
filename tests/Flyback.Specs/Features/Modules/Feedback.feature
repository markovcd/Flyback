Feature: Feedback shows the previous frame
  The camera pointed at its own monitor. Feedback reads the whole of the
  previous frame, anywhere on it; a loop in the patch reads only this spot's.

  Specified by ADR-0012, with ADR-0075 for loops.

  Scenario: Before the first frame there is nothing to feed back
    Given feedback shown on the screen
    Then the patch is accepted without complaint
    And the screen is black

  Scenario: Each frame builds on the one before it
    Given feedback brightened by 0.1 each frame
    Then each frame builds on the last: 0.1, 0.2, 0.3

  Scenario: Rewinding clears what has built up
    Given feedback brightened by 0.1 each frame
    Then after 5 frames and a rewind the next frame is back at 0.1

  Scenario: A trail fades by the second, whatever the frame rate
    Given a white flash on the first frame, left to trail at a persist of 0.99
    Then a second later the trail is as faint at 30 frames a second as at 60
