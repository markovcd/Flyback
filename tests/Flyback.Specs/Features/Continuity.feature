Feature: Editing a patch while it plays does not restart it
  Turning a knob changes the sound from where it had got to. An oscillator keeps
  its place in the wave and a loop keeps what it last made.

  Specified by ADR-0067, with ADR-0021 for the rebuild every edit makes.

  # At 0.125 s a 10 Hz sine is at its crest, so a restart would drop straight to
  # nothing.
  Scenario: Turning a tone's frequency while it plays does not click
    Given a 10 Hz sine is playing
    When it has played 0.125 seconds
    And its frequency is turned to 12 Hz
    And it plays on for 0.1 seconds
    Then the sound never clicks

  # Settled at a half, the loop's next sample is half of that plus the new 0.3.
  # Forgotten, it would be 0.3 alone.
  Scenario: Turning a knob inside a loop keeps what the loop had built up
    Given a loop that halves what it made last and adds a quarter
    And the loop is heard at the speakers
    When the loop has played until it settles
    And what it adds is turned to 0.3
    And it plays one more sample
    Then that sample is about 0.55
