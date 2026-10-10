Feature: What plays the sound is named beside what draws the picture
  Somebody chasing a stutter wants to know what the sound goes through as much as
  what draws the picture. Wherever Flyback names the renderer it names the sound
  backend beside it, as the backend names itself: ASIO, JACK, WASAPI and the rest.

  Scenario: The status bar names the sound backend after the renderer
    Given a 220 Hz sine is playing
    And the speakers play whatever they are handed
    And the patch is open in the editor
    When the speakers have played for 0.1 seconds
    Then the status bar says the sound plays through "Loopback"

  Scenario: The viewer's stats line names the sound backend
    Given a rainbow across the screen
    And a 220 Hz sine is heard with it
    When the viewer plays it through "JACK" with "--stats"
    Then the viewer's picture says the sound plays through "JACK"

  Scenario: The viewer's report names the sound backend
    Given a rainbow across the screen
    And a 220 Hz sine is heard with it
    When the viewer plays it through "ASIO" with "--report"
    Then the viewer's report says the sound played through "ASIO"

  Scenario: A run with nothing to play through names no sound backend
    Given a rainbow across the screen
    When the viewer plays it with "--stats"
    Then the viewer's picture names no sound backend
