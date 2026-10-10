Feature: The command line says whether a patch works, and whether two are the same
  Somebody with no window, a script or an agent, asks flyback-cli about a patch
  file. The exit code is the answer, and what is wrong is said by its line.
  flyback-cli check compiles it, flyback-cli info says what it is,
  flyback-cli measure what its outputs carry and flyback-cli compare whether two
  are the same instrument; flyback-cli print, flyback-cli pack and
  flyback-cli save write it out, and flyback-cli shot draws the editor around it.

  Scenario: A patch that works passes the check
    Given a 220 Hz sine is playing
    And the patch is saved as "tone.fbk"
    When flyback-cli checks "tone.fbk"
    Then the command succeeds

  Scenario: A patch with a mistake fails the check, and says which line
    Given the text saved as "broken.fbks":
      """
      rings() |> out.color
      kaleidoscop() |> out.color
      """
    When flyback-cli checks "broken.fbks"
    Then the command says the patch has problems
    And it points at line 2

  Scenario: Measuring a patch says what an output nothing is wired to carries
    Given rings on the screen beside a 2 Hz sine called "lfo" that nothing is wired to, saved as "lfo.fbks"
    When flyback-cli measures "lfo.fbks"
    Then the command succeeds
    And it says "lfo.out" swings from -1 to 1, 2 times a second

  Scenario: A shipped preset is described by its name, with no file saved first
    When flyback-cli describes the preset "Plasma"
    Then the command succeeds
    And it says what the picture costs

  Scenario: Describing a patch says how long it plays
    Given a patch that plays for 1:30.50, saved as "piece.fbks"
    When flyback-cli describes "piece.fbks"
    Then the command succeeds
    And it says the patch plays for 1:30.50

  Scenario: Describing a patch that sets no length says so
    Given a patch that sets no length, saved as "drone.fbks"
    When flyback-cli describes "drone.fbks"
    Then the command succeeds
    And it says the patch sets no length

  Scenario: A shipped preset packs into a bundle with the files it carries
    When flyback-cli packs the preset "Mycelium"
    Then the command succeeds
    And the bundle holds the preset and every file it carries

  Scenario: A shipped preset is saved as a patch file that opens as the preset
    When flyback-cli saves the preset "Acid" as "acid.fbk"
    Then the command succeeds
    And "acid.fbk" opens as the preset "Acid"

  Scenario: A preset that plays recordings it carries is saved as a bundle to keep them
    When flyback-cli saves the preset "Mycelium" as "mycelium.fbk"
    Then the command says to save it as a bundle to take its recordings along

  Scenario: A preset nobody shipped is refused with the names of the ones there are
    When flyback-cli prints the preset "Plasm"
    Then the command fails, listing the presets there are

  Scenario: A shot is the editor's window, with the picture at the second asked for
    Given a picture that turns from black to white at 2 seconds, saved as "dawn.fbks"
    When flyback-cli shoots "dawn.fbks" at 1 second
    Then the command succeeds
    And the shot is 1440 by 900 with a black picture in it
    When flyback-cli shoots "dawn.fbks" at 3 seconds
    Then the shot has a white picture in it

  Scenario: A shot told to use an editor that is not there says so
    Given a 110 Hz sine saved as "tone.fbks"
    When flyback-cli shoots "tone.fbks" with the editor "nowhere/Flyback"
    Then the command says the editor is not there

  Scenario: A shot can open the assistant's column beside the canvas
    Given a 110 Hz sine saved as "tone.fbks"
    When flyback-cli shoots "tone.fbks" at 1 second
    Then the shot has no assistant's column
    When flyback-cli shoots "tone.fbks" at 1 second, with the assistant's column open
    Then the shot has the assistant's column at its left

  Scenario: A cropped shot is the canvas around the modules, for a text patch too
    Given a 110 Hz sine saved as "tone.fbks"
    When flyback-cli shoots "tone.fbks" at 1 second, cropped to the modules
    Then the command succeeds
    And the shot is two modules side by side, smaller than the window

  Scenario: A patch saved twice is the same instrument
    Given a 220 Hz sine is playing
    And the patch is saved as "first.fbk"
    And the patch is saved as "second.fbk"
    When flyback-cli compares "first.fbk" with "second.fbk"
    Then the command succeeds
    And it says they are the same instrument

  Scenario: A patch with a different pitch is a different instrument
    Given a 220 Hz sine is playing
    And the patch is saved as "low.fbk"
    And its frequency is turned to 330 Hz
    And the patch is saved as "high.fbk"
    When flyback-cli compares "low.fbk" with "high.fbk"
    Then the command says the patch has problems
    And it says they are not the same instrument
