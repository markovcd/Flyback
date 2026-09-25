Feature: A patch says how long it plays
  A piece has a length, to a hundredth of a second, and the seek bar spans it. It
  is part of the patch, so it survives being saved, opened and written out as text.

  Scenario: A patch keeps its length when it is saved, opened and written out as text
    Given the text:
      """
      length 1:30.50
      sine(freq: 220) |> out.left
      """
    Then the patch plays for 90.5 seconds
    When the patch is saved and opened again
    Then the patch plays for 90.5 seconds
    When the patch is written out as text and read back
    Then the patch plays for 90.5 seconds

  Scenario: A patch that says nothing about its length plays for three minutes
    Given the text:
      """
      sine(freq: 220) |> out.left
      """
    Then the patch plays for 180 seconds
