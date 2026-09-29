Feature: The web viewer
  A patch opened in a browser plays the sound it plays on the desktop, shipped
  plugins' modules included.

  Scenario Outline: A preset sounds in the browser as it does on the desktop
    Given the shipped preset "<preset>"
    When it plays in the web viewer for 1 second
    Then its sound is the desktop's to within one step of 16 bits

    Examples:
      | preset           |
      | Sidebands        |
      | Beat you can see |

  Scenario: The preset site serves the web viewer
    When someone opens the web viewer on the preset site
    Then its page and everything it loads to start are there
    And a browser that kept an earlier build of it asks for this one
