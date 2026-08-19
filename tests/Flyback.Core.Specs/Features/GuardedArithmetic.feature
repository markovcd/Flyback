Feature: Degenerate arithmetic yields zero rather than NaN
  A patch is edited live, and half of the states it passes through on the way to
  a finished one are degenerate: a Divide sits at zero until its divisor is
  wired, a Log receives a signal that swings negative. IEEE 754 says those
  produce NaN, and NaN propagates — one of them anywhere upstream turns the
  whole image black and keeps it black, with nothing to say which of thirty
  modules caused it.

  Every scenario here adds a known offset downstream of the degenerate module,
  because that is what tells the two outcomes apart: a guarded zero comes
  through as that offset, and a NaN comes through as black.

  Specified by ADR-0013.

  # The half-wired Divide is downstream of a working image and upstream of the
  # screen, which is where an unguarded one does its damage: an infinity added
  # to a color takes the whole frame with it, not just the branch it is on.
  Scenario: A patch mid-edit shows an image rather than going black
    Given a patch containing:
      | name     | module       |
      | coords   | coord        |
      | tint     | color.hsv   |
      | broken   | math.div     |
      | brighten | color.gain  |
      | screen   | output       |
    And "coords" output "x" is wired to "tint" input "hue"
    And "broken" input "a" is set to 1
    And "broken" input "b" is set to 0
    And "tint" output "color" is wired to "brighten" input "color"
    And "brighten" input "gain" is set to 1
    And "broken" output "out" is wired to "brighten" input "bias"
    And "brighten" output "color" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues
    And the rendered image is not black
    And the centre pixel is about 1, 0.333, 0

  # Dividing by zero yields zero, which is wrong mathematically and useful in
  # practice: the rest of the patch stays visible while the divisor is wired up.
  Scenario Outline: A degenerate calculation over two inputs yields zero
    Given a patch containing:
      | name   | module       |
      | maths  | <module>     |
      | offset | math.add     |
      | screen | output       |
    And "maths" input "a" is set to <a>
    And "maths" input "b" is set to <b>
    And "maths" output "out" is wired to "offset" input "a"
    And "offset" input "b" is set to 0.5
    And "offset" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues
    And the centre pixel is about 0.5, 0.5, 0.5

    Examples:
      | module   | a  | b   | what it would otherwise be         |
      | math.div | 1  | 0   | infinity                           |
      | math.mod | 1  | 0   | NaN                                |
      | math.pow | -2 | 0.5 | NaN, a negative root               |

  Scenario Outline: A function outside its domain yields zero
    Given a patch containing:
      | name   | module       |
      | maths  | <module>     |
      | offset | math.add     |
      | screen | output       |
    And "maths" input "in" is set to <in>
    And "maths" output "out" is wired to "offset" input "a"
    And "offset" input "b" is set to 0.5
    And "offset" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues
    And the centre pixel is about 0.5, 0.5, 0.5

    # The overflow case is stated in terms of the register width, which ADR-0032
    # widened: exp overflows a float past about 88 and a double past about 710.
    Examples:
      | module    | in   | what it would otherwise be |
      | math.sqrt | -1   | NaN                        |
      | math.log  | 0    | negative infinity          |
      | math.exp  | 1000 | infinity, overflowed       |

  # Clamp is the one that would throw rather than return NaN: Math.Clamp rejects
  # a range whose low is above its high. Holding it at the low end is arbitrary,
  # but it is a number, and an editor cannot afford the exception while someone
  # is mid-drag on the knob that inverted the range.
  Scenario: An inverted Clamp range holds the signal rather than throwing
    Given a patch containing:
      | name   | module       |
      | knob   | value        |
      | hold   | math.clamp   |
      | screen | output       |
    And "knob" input "value" is set to 0.75
    And "knob" output "out" is wired to "hold" input "in"
    And "hold" input "low" is set to 0.25
    And "hold" input "high" is set to -1
    And "hold" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues
    And the centre pixel is about 0.25, 0.25, 0.25

  # The guards matter most where a value persists. Everything else in the
  # machine forgets a bad number on the next evaluation, but the feedback buffer
  # would carry it into every later frame — so an edit that has since been
  # undone would leave a stain that outlives it.
  Scenario: A guard that fires leaves no stain on the feedback history
    Given a patch containing:
      | name     | module       |
      | previous | feedback     |
      | broken   | math.div     |
      | offset   | math.add     |
      | brighten | color.gain  |
      | screen   | output       |
    And "broken" input "a" is set to 1
    And "broken" input "b" is set to 0
    And "broken" output "out" is wired to "offset" input "a"
    And "offset" input "b" is set to 0.1
    And "previous" output "color" is wired to "brighten" input "color"
    And "brighten" input "gain" is set to 1
    And "offset" output "out" is wired to "brighten" input "bias"
    And "brighten" output "color" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues
    And rendering 1 frame gives a centre brightness of about 0.1
    And rendering 2 frames gives a centre brightness of about 0.2
    And rendering 3 frames gives a centre brightness of about 0.3
