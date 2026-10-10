Feature: A patch says who made it and how to find it
  A patch carries who made it and a few words to find it by. They are part of
  the patch, so they survive being saved, opened and written out as text.

  Scenario: A patch keeps who made it and its tags when it is saved and opened again
    Given the text:
      """
      author "Ada"
      tags "drone" "Slow Build"
      sine(freq: 220) |> out.left
      """
    Then the patch says it was made by "Ada"
    And the patch is tagged "drone, slow-build"
    When the patch is saved and opened again
    Then the patch says it was made by "Ada"
    And the patch is tagged "drone, slow-build"
    When the patch is written out as text and read back
    Then the patch says it was made by "Ada"
    And the patch is tagged "drone, slow-build"
