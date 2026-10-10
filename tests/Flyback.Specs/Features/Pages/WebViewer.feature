Feature: The web viewer
  A patch opened in a browser plays the sound it plays on the desktop, shipped
  plugins' modules included.

  @node
  Scenario Outline: A preset sounds in the browser as it does on the desktop
    Given the shipped preset "<preset>"
    When it plays in the web viewer for 1 second
    Then its sound is the desktop's to within one step of 16 bits

    Examples:
      | preset           |
      | Sidebands        |
      | Beat you can see |
      | Duck             |

  @node
  Scenario: A sound worked out a step lower in the browser is the desktop's at that rate
    Given the shipped preset "Sidebands"
    When it plays in the web viewer for 1 second worked out at 1 times the output rate
    Then its sound is the desktop's at that rate, to within one step of 16 bits

  @node
  Scenario: The web viewer judges how its sound keeps pace by the chunks it plays, not its warm-up
    Given the shipped preset "Sidebands"
    When it plays in the web viewer for 1 second after a warm-up of 1 second
    Then every chunk it played was timed, and none of the warm-up
    And its sound is the desktop's to within one step of 16 bits

  @node
  Scenario: A sound the web viewer keeps up with is left where it is
    Given the shipped preset "Sidebands"
    When it plays in the web viewer for 6 seconds worked out at 4 times the output rate, judging itself as it plays
    Then it is still worked out at 4 times the output rate, and not said to be behind

  @node
  Scenario: A panel knob turns the sound in the browser as it does on the desktop
    Given the shipped preset "Vigil"
    When it plays in the web viewer for 1 second with its "fog" knob at 0
    Then its sound is the desktop's with the same knob turned, to within one step of 16 bits
    And it is not the sound with the knob where it rests

  @node
  Scenario: A note played on the computer keyboard sounds in the browser as it does on the desktop
    Given the shipped preset "Played"
    When it plays in the web viewer for 1 second with note 60 held from 0.1 to 0.6 seconds
    Then its sound is the desktop's with the same note played, to within one step of 16 bits
    And it is not the sound with nothing played

  @node
  Scenario: A patch that says no length plays on in the web viewer
    Given the shipped preset "Sidebands"
    When it plays in the web viewer for 0.1 seconds
    Then the web viewer gives it no length, so no end and no seek bar

  @node
  Scenario: A patch needing a plugin the web viewer lacks is refused, naming the plugin
    When a patch needing the "Lantern" plugin is opened in the web viewer
    Then the web viewer refuses it, naming "Lantern"

  @node
  Scenario: A Scope in the web viewer charts the sound the desktop's Scope charts
    Given the shipped preset "Duck"
    When it plays in the web viewer for 1 second, keeping what its sound hands the picture
    Then the picture is handed the Scope's chart the desktop draws, to within one step of 16 bits

  @node
  Scenario: The web viewer says what a preset is for
    Given the shipped preset "Sidebands"
    When it plays in the web viewer for 0.1 seconds
    Then the web viewer says what the preset is for

  @node
  Scenario: The web viewer knows a preset has sound
    Given the shipped preset "Sidebands"
    When it plays in the web viewer for 0.1 seconds
    Then the web viewer says it has sound

  @node
  Scenario: The web viewer knows a patch has no sound
    Given a rainbow across the screen
    When the patch plays in the web viewer for 0.1 seconds
    Then the web viewer says it has no sound

  @node
  Scenario: The web viewer offers the presets the editor does
    Given every preset the shipped plugins add as well
    When the web viewer lists its presets
    Then they are the editor's, in its order, under its headings and with their descriptions, less the blank canvas

  Scenario: The web viewer's page and scripts are there
    When someone opens the web viewer on the preset site
    Then its page and everything it loads to start are there

  Scenario: The web viewer leaves picking a preset to the presets page
    When someone opens the web viewer on the preset site
    Then it offers no presets of its own, only a way back to the presets page
