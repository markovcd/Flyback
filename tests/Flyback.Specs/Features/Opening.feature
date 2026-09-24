Feature: A patch opened plays compiled from its beginning
  A preset or a file that is opened is not played on the interpreter while its
  compiled code is built. It waits, silent and with its clock stopped, and starts
  from the beginning once it runs compiled. An edit to a patch already playing
  still plays at once.

  Specified by ADR-0076.

  Scenario: An opened preset is heard from its beginning
    Given the "Drone" preset is opened with the sound on
    When it is listened to for 0.5 seconds
    Then what it played is the preset from its beginning
