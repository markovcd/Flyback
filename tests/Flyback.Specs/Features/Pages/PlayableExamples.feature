@hugo
Feature: Every example the website lets a visitor play is a whole patch
  A code example on the website is playable when its fence or its pre is marked as a
  whole patch. A mark on a fragment would open a player that draws nothing and says
  nothing, so each marked example has to compile and reach the picture or the sound.

  @unmeasured
  Scenario Outline: The examples on <page> compile and make a picture or a sound
    When the examples marked playable on "<page>" are read
    Then there are some, and each is a patch that compiles and reaches the picture or the sound

    Examples:
      | page                |
      | /index.html         |
      | /docs/language.html |
      | /embed.html         |

  @unmeasured
  Scenario: The language reference keeps its fragments as plain colored code
    When the examples marked playable on "/docs/language.html" are read
    Then the page also has colored examples that are not playable
