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

  # Time's progress runs 0 to 1 across the length, so a fade or a sweep follows
  # the length when it changes.
  Scenario: The clock says how far through its length the patch is
    Given a patch 4 seconds long that plays how far through its length it is
    Then the sound is about 0.25 at 1 seconds
    And the sound is about 0.75 at 3 seconds
    When the patch is written out as text and read back
    Then the sound is about 0.75 at 3 seconds

  Scenario: The clock says how long the patch plays for
    Given a patch that sets no length and plays how long it is
    Then the sound says the patch plays for 180 seconds
