Feature: A saved patch opens as it was saved
  A patch file is the patch, not a picture of it. Opening one that this build
  cannot fully read still opens everything it can, and says what is missing.

  Specified by ADR-0020, and by ADR-0026 for modules this build does not have.

  Scenario: Saving and opening a patch changes nothing
    Given a rainbow across the screen
    When the patch is saved and opened again
    Then it opens complete
    And the picture is as it was

  Scenario: A patch naming a module this build lacks still opens, and says so
    Given a saved rainbow that also names a module from a newer Flyback
    When the file is opened
    Then it opens with a note that it uses 1 module this build does not have
    And the picture is as it was

  Scenario: A patch saved by a newer version of Flyback is flagged
    Given a rainbow saved in a file layout newer than this build reads
    When the file is opened
    Then it opens with a note that it was saved by a newer version of Flyback
