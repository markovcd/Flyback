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
      | Duck             |

  Scenario: A panel knob turns the sound in the browser as it does on the desktop
    Given the shipped preset "Vigil"
    When it plays in the web viewer for 1 second with its "fog" knob at 0
    Then its sound is the desktop's with the same knob turned, to within one step of 16 bits
    And it is not the sound with the knob where it rests

  Scenario: A note played on the computer keyboard sounds in the browser as it does on the desktop
    Given the shipped preset "Played"
    When it plays in the web viewer for 1 second with note 60 held from 0.1 to 0.6 seconds
    Then its sound is the desktop's with the same note played, to within one step of 16 bits
    And it is not the sound with nothing played

  Scenario: A patch that says no length plays on in the web viewer
    Given the shipped preset "Sidebands"
    When it plays in the web viewer for 0.1 seconds
    Then the web viewer gives it no length, so no end and no seek bar

  Scenario: A patch needing a plugin the web viewer lacks is refused, naming the plugin
    When a patch needing the "Lantern" plugin is opened in the web viewer
    Then the web viewer refuses it, naming "Lantern"

  Scenario: A picture only the processor can draw is left out in the web viewer, saying why
    Given the shipped preset "Duck"
    When its picture is opened in the web viewer
    Then the web viewer leaves the picture out, saying it cannot draw a Scope

  Scenario: A picture the shader draws is drawn in the web viewer
    Given the shipped preset "Beat you can see"
    When its picture is opened in the web viewer
    Then the web viewer draws the picture

  Scenario: The web viewer says what a preset is for
    Given the shipped preset "Sidebands"
    When it plays in the web viewer for 0.1 seconds
    Then the web viewer says what the preset is for

  Scenario: The web viewer offers the presets the editor does
    Given every preset the shipped plugins add as well
    When the web viewer lists its presets
    Then they are the editor's, in its order, under its headings and with their descriptions, less the blank canvas

  Scenario: The preset site serves the web viewer
    When someone opens the web viewer on the preset site
    Then its page and everything it loads to start are there

  Scenario: A phone's screen stays on while the web viewer plays full screen
    When someone opens the web viewer on the preset site
    Then it keeps the screen on while its picture has the whole screen and plays

  Scenario: The web viewer leaves picking a preset to the presets page
    When someone opens the web viewer on the preset site
    Then it offers no presets of its own, only a way back to the presets page
    And a browser that kept an earlier build of it asks for this one
