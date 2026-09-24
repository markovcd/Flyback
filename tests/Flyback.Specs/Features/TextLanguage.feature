Feature: A patch can be written as text
  The text language is a second way to write the same patch: anything built on
  the canvas can be written out, and anything written reads back as the patch it
  describes.

  Specified by ADR-0065.

  Scenario: A patch written as text plays
    Given the text:
      """
      sine(freq: 220) |> out.left
      """
    Then it reads without complaint
    And the speakers are not silent

  Scenario: A patch written out as text reads back as the same patch
    Given a rainbow across the screen
    When the patch is written out as text and read back
    Then the picture is as it was

  # One mistake does not lose the rest of the patch.
  Scenario: A mistake is pointed out on its own line, and the rest still plays
    Given the text:
      """
      rings() |> out.color
      kaleidoscop() |> out.color
      """
    Then the complaint quotes line 2
    And it suggests "kaleidoscope"
    And the screen is not black

  # A second binding would leave every reader of the name guessing which it means.
  Scenario: A name is bound once
    Given the text:
      """
      let hum = sine(freq: 110)
      let hum = sine(freq: 220)
      hum |> out.left
      """
    Then the complaint quotes line 2
    And the complaint says "already bound on line 1"

  Scenario: The clock cannot be renamed into something else
    Given the text:
      """
      let t = 5
      x |> sine(freq: 2, phase: t) |> out.left
      """
    Then the complaint quotes line 1
    And the complaint says "the clock"

  Scenario: Wiring a socket twice is pointed out rather than quietly replaced
    Given the text:
      """
      rings(freq: 3) |> out.color
      rings(freq: 5) |> out.color
      """
    Then the complaint quotes line 2
    And the complaint says "already wired on line 1"
