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

  Scenario: Measuring again takes up-to-date labels down
    Given an LFO turning 2 times a second, wired to nothing
    And the patch is open in the editor
    When the patch is measured
    And Measure is pressed again
    Then nothing is pinned

  Scenario: Settings say how long Measure runs
    Given an LFO turning 2 times a second, wired to nothing
    And the patch is open in the editor
    And the settings say to measure for 8 seconds
    When the patch is measured
    Then the measurement covers 8 seconds

  Scenario: A measured color shows the picture it made
    Given a color that follows the picture's x, wired to nothing
    And the patch is open in the editor
    When the patch is measured
    Then the color's measurement holds its picture, dark on the left and bright on the right
