Feature: Measure pins what each output carries beside it
  While building a patch, somebody can see what comes out of every output,
  wired to anything or not: its value, or its range and how fast it moves,
  until an edit makes it out of date.

  Scenario: An LFO wired to nothing is measured at its rate
    Given an LFO turning 2 times a second, wired to nothing
    And the patch is open in the editor
    When the patch is measured
    Then the LFO is pinned as swinging from -1 to 1, 2 times a second

  Scenario: An edit greys the measurement until the next
    Given an LFO turning 2 times a second, wired to nothing
    And the patch is open in the editor
    When the patch is measured
    And the LFO is turned to 5 times a second
    Then the measurement is marked out of date
