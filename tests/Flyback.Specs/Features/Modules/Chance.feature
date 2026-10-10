Feature: Chance lets some notes through and not others
  A Chance flips a coin for each note on its gate. The note plays on its gate
  as often as the knob says and on its else the rest of the time, and either
  way it plays whole.

  Scenario: At even chance about half the notes play
    Given the gate of a sequencer stepping 16 times a second, through a chance of 0.5
    When it plays for 20 seconds
    Then about half of its notes play
    And every note that plays, plays whole

  Scenario: At no chance nothing plays
    Given the gate of a sequencer stepping 16 times a second, through a chance of 0
    When it plays for 20 seconds
    Then none of its notes play

  Scenario: At full chance everything plays
    Given the gate of a sequencer stepping 16 times a second, through a chance of 1
    When it plays for 20 seconds
    Then every one of its notes plays

  Scenario: The notes that do not play on the gate play on the else
    Given the gate of a sequencer stepping 16 times a second, through a chance of 0.3, with both of its gates added together
    When it plays for 20 seconds
    Then every one of its notes plays
    And every note that plays, plays whole
