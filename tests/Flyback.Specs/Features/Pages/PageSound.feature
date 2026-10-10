@node
Feature: The editor in a page plays its sound
  A page hands each edit to the web viewer's worker, which plays it on from where
  the sound had got to, keeping what the patch remembers, as the desktop's engine does.

  Scenario: An edit in a page is heard as it is on the desktop
    Given the shipped preset "Beat you can see"
    When it plays in the web editor for 1 second, its Output's volume set to 0.2 by an edit at 0.5 seconds
    Then its sound is the desktop's through the same edit, to within one step of 16 bits
    And it is not the sound with nothing edited
    And every chunk heard, before the edit and after, was timed
