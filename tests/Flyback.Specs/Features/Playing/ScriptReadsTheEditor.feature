Feature: A script reads how the editor is doing without looking at it
  An editor nobody watches, in a page or on an Android device over adb, answers a script
  with what it has open, what draws the picture and how fast, what runs the sound and how
  fast, and the last thing it said. A session confirms the sound plays at speed in one
  question instead of reading the status bar off a screenshot.

  Scenario: The script is told the sound runs as IL, and how fast
    Given a 220 Hz sine is playing
    And the speakers play whatever they are handed
    And the patch is open in the editor
    And the patch is playing
    When the speakers have played long enough to time the sound
    And a script asks the editor how it is doing
    Then the script is told the sound runs on "IL"
    And the script is told the sound renders faster than real time

  Scenario: An editor started on the interpreter says so
    Given a 220 Hz sine is playing
    And the speakers play whatever they are handed
    And the editor is started on the interpreter
    And the patch is open in the editor
    And the patch is playing
    When the speakers have played until the sound's program is in
    And a script asks the editor how it is doing
    Then the script is told the sound runs on "interpreter"
    And the script is told nothing of how fast the sound renders
