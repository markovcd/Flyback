Feature: A Bridge carries a value the sound works out into the picture
  A Bridge reads a value off the sound once a frame and hands it to the picture:
  an envelope, a slew, a count or a phase, as the speakers have it. It keeps the
  value's sign, and with 'wrap' set a phase that crosses its top reads as just
  past the top rather than halfway round.

  Specified by ADR-0238.

  Scenario: A value below nought reaches the picture with its sign
    Given the text saved as "signed.fbks":
      """
      let carried = value(-0.25) |> bridge()
      carried + 0.5 |> out.color
      """
    When flyback-cli draws a still of "signed.fbks" at 1 second
    Then the still is an even gray of 0.25

  Scenario: A phase that has just wrapped reaches the picture beside its top, not halfway round
    Given the text saved as "phase.fbks":
      """
      let phase = expression(a: t, formula: "fract(a)") |> bridge(wrap: 1)
      expression(a: phase, formula: "abs(fract(a + 0.5) - 0.5) * 2") |> out.color
      """
    When flyback-cli draws a still of "phase.fbks" at 1.01 seconds
    Then the still is an even gray of 0
