Feature: A patch finds its files in the library folder
  The library folder, set in the Files section of the settings, is where a sound
  or a picture a patch names is looked for when it is not beside the patch. A
  file chosen from inside it is named from it, so a patch finds it on any machine
  with the same library.

  Scenario: A picture that is not beside the patch is found in the library folder
    Given a picture "moon.png" in the library folder
    And a saved patch showing "moon.png", with no such picture beside it
    When the patch is opened with the library folder
    Then it compiles with nothing wrong

  Scenario: A picture beside the patch comes before the library folder's
    Given a picture "moon.png" in the library folder
    And a saved patch showing "moon.png", with a picture of that name beside it
    When the patch is opened with the library folder
    Then the picture shown is the one beside the patch

  Scenario: A picture chosen from the library folder is named from it
    Given a picture "stars/moon.png" in the library folder
    When that picture is chosen for a picture module
    Then the patch names it "stars/moon.png"
