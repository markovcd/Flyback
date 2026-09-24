Feature: The command line says whether a patch works, and whether two are the same
  Somebody with no window, a script or an agent, asks flyback-cli about a patch
  file. The exit code is the answer, and what is wrong is said by its line.

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
