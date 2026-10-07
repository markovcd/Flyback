Feature: A tutorial ends with the preset it builds
  A tutorial that builds a patch ends with the whole patch as text, and the same patch
  is in the preset gallery, so a reader can open the finished one beside their own.
  The text on the page and the preset are one instrument.

  Scenario Outline: The patch a tutorial builds is the preset it names
    Given the patch the tutorial "<page>" ends with
    Then it is the same instrument as the shipped preset "<preset>"

    Examples:
      | page                  | preset           |
      | tutorials/echoes.html | Two echoes       |
