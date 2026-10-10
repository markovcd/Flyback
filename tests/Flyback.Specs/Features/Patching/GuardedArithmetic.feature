Feature: Impossible arithmetic gives zero instead of blacking out the picture
  Half the states a patch passes through while being edited are degenerate: a
  Divide sits at zero until its divisor is wired, a Log receives a signal that
  swings negative. Left to IEEE 754 those make NaN, and one NaN anywhere turns
  the whole picture black with nothing to say which module did it.

  Specified by ADR-0013.

  Scenario: A half-wired Divide leaves the picture showing
    Given a rainbow across the screen, brightened by one divided by zero
    Then the patch is accepted without complaint
    And the screen shows 1, 0.333, 0

  # Everything else forgets a bad number on the next evaluation, but feedback
  # would carry it into every later frame.
  Scenario: Impossible arithmetic leaves no stain on feedback
    Given feedback brightened each frame by 0.1 plus one divided by zero
    Then each frame builds on the last: 0.1, 0.2, 0.3
