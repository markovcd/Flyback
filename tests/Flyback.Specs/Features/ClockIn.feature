Feature: A patch keeps time with a drum machine
  A Clock In follows the MIDI clock of a drum machine or a sequencer. Its beats
  count from the machine's Start at whatever tempo the machine is sending, hold
  while it is stopped, and begin again when it starts over.

  Scenario: A sequencer steps with the machine's beats
    Given a sequencer of 0.2, 0.4, 0.6, 0.8 stepping once a beat of a drum machine
    When the drum machine starts and plays 2 beats at 120 bpm
    Then the sound is about 0.2 at 0.25 seconds
    And the sound is about 0.4 at 0.75 seconds

  Scenario: Stopping the machine holds the beat
    Given the beats of a drum machine's clock on the speakers
    When the drum machine starts and plays 1 beat at 120 bpm
    And the drum machine stops
    And it plays on for 0.9 seconds
    Then the sound is about 1 at 1.4 seconds

  Scenario: Starting again counts from the top
    Given the beats of a drum machine's clock on the speakers
    When the drum machine starts and plays 2 beats at 120 bpm
    And the drum machine starts and plays 1 beat at 120 bpm
    And the drum machine stops
    Then the sound is about 1 at 1.6 seconds

  Scenario: The tempo the machine sends is on the bpm output
    Given the bpm of a drum machine's clock on the speakers
    When the drum machine starts and plays 2 beats at 140 bpm
    Then the sound is about 140 at 0.8 seconds

  Scenario: A clock left to follow whatever is plugged in has nothing to say when nothing is
    Given the beats of a clock left to follow whatever is plugged in, on the speakers
    Then the patch is accepted without complaint

  Scenario: A clock set to follow the computer keyboard is told it keeps no clock
    Given the beats of a clock following the computer keyboard, on the speakers
    Then Flyback says the computer keyboard keeps no clock
