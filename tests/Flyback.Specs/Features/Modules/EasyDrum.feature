Feature: Easy Drum: a drum that plays in time on its own
  The Easy plugin's Easy Drum is a drum machine's voice in one module. Picked by
  its sound, it plays the rhythm that sound is usually heard in, at a tempo,
  with nothing wired; on Trigger it waits to be struck. No knob, wire or
  setting makes it louder than full scale.

  Scenario: With nothing wired it plays a kick on every beat
    Given an Easy Drum with nothing wired
    Then the sound is heard
    And it plays at 0, 0.5, 1 and 1.5 seconds in the first two

  Scenario: A snare left on its own plays on two and four
    Given an Easy Drum playing "snare"
    Then it plays at 0.5 and 1.5 seconds in the first two

  Scenario: An open hat left on its own plays the offbeats
    Given an Easy Drum playing "open hat"
    Then it plays at 0.25, 0.75, 1.25 and 1.75 seconds in the first two

  Scenario: On Trigger it waits to be struck
    Given an Easy Drum on Trigger with nothing wired
    Then the sound is silent

  Scenario: Every knob turned all the way up is still inside full scale
    Given an Easy Drum with every knob turned all the way up
    Then the sound is heard
    And the sound never goes past full scale
