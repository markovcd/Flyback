Feature: A Counter counts triggers and comes round
  A Counter counts each rise of its trigger and comes round to nought every
  so many, so a patch can tell the beat of a bar, the kick of a phrase or the
  phrase of a song. Its wrap passes the trigger that brings it round, so one
  Counter can count another's rounds.

  Scenario: A counter of four counts the beats of a bar
    Given a counter of 4 counting the gate of a sequencer stepping 2 times a second
    Then the sound is about 1 at 0.25 seconds
    And the sound is about 2 at 0.75 seconds
    And the sound is about 3 at 1.25 seconds
    And the sound is about 0 at 1.75 seconds
    And the sound is about 1 at 2.25 seconds

  Scenario: A second counter counts the bars of the first
    Given a counter of 4 counting the gate of a sequencer stepping 2 times a second, its rounds counted by a second counter
    Then the sound is about 0 at 1.25 seconds
    And the sound is about 1 at 1.75 seconds
    And the sound is about 1 at 3.25 seconds
    And the sound is about 2 at 3.75 seconds

  Scenario: Reset starts the count again
    Given a counter of 8 counting the gate of a sequencer stepping 2 times a second, reset while the time is between 1.6 and 1.8 seconds
    Then the sound is about 3 at 1.25 seconds
    And the sound is about 0 at 1.75 seconds
    And the sound is about 1 at 2.25 seconds
