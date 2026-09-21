Feature: Impossible arithmetic gives zero instead of blacking out the picture
  Half the states a patch passes through while being edited are degenerate: a
  Divide sits at zero until its divisor is wired, a Log receives a signal that
  swings negative. Left to IEEE 754 those make NaN, and one NaN anywhere turns
  the whole picture black with nothing to say which module did it.

  Each scenario adds one half after the calculation: a guarded zero comes
  through as that half, a NaN as black.

  Specified by ADR-0013.

  Scenario: A half-wired Divide leaves the picture showing
    Given a rainbow across the screen, brightened by one divided by zero
    Then the patch is accepted without complaint
    And the screen shows 1, 0.333, 0

  Scenario Outline: A calculation with no answer gives zero
    Given the <calculation> of <a> and <b> plus one half on the screen
    Then the screen shows 0.5

    Examples:
      | calculation | a  | b   | what it would otherwise be |
      | quotient    | 1  | 0   | infinity                   |
      | remainder   | 1  | 0   | NaN                        |
      | power       | -2 | 0.5 | NaN, a negative root       |

  # exp overflows a double past about 710.
  Scenario Outline: A function outside its domain gives zero
    Given the <function> of <in> plus one half on the screen
    Then the screen shows 0.5

    Examples:
      | function    | in   | what it would otherwise be |
      | square root | -1   | NaN                        |
      | logarithm   | 0    | negative infinity          |
      | exponential | 1000 | infinity, overflowed       |

  # Holding at the low end is arbitrary, but it is a number, and nobody mid-drag
  # on the knob that inverted the range can afford an exception.
  Scenario: A Clamp whose range is upside down holds the signal at its low end
    Given a level of 0.75 clamped between 0.25 and -1
    Then the screen shows 0.25

  # Everything else forgets a bad number on the next evaluation, but feedback
  # would carry it into every later frame.
  Scenario: Impossible arithmetic leaves no stain on feedback
    Given feedback brightened each frame by 0.1 plus one divided by zero
    Then each frame builds on the last: 0.1, 0.2, 0.3
