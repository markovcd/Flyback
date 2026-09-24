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
