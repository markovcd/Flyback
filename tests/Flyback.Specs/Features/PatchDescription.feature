Feature: A patch says what it is for
  A patch carries a line of prose saying what it is for. It is part of the
  patch, so it survives being saved, opened and written out as text, and it is
  what the preset gallery shows under a preset's name.

  Scenario: A description written in the text stays with the patch
    Given the text:
      """
      description "A hum, and nothing else."
      sine(freq: 220) |> out.left
      """
    Then the patch says it is "A hum, and nothing else."
    When the patch is saved and opened again
    Then the patch says it is "A hum, and nothing else."
    When the patch is written out as text and read back
    Then the patch says it is "A hum, and nothing else."

  Scenario: Every shipped preset opens saying what it is for
    Given every shipped preset
    Then each one opens saying what it is for
