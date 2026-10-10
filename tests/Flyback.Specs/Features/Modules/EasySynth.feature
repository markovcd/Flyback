Feature: Easy Synth: a synth that cannot be set wrong
  The Easy plugin, which the preset site starts with, has Easy Synth: a whole
  synth in one module. It plays before anything is wired, it plays the note it
  is given, and no knob, wire or setting makes it louder than full scale.

  Scenario: With nothing wired it plays
    Given an Easy Synth with nothing wired
    Then the sound is heard
    And the sound never goes past full scale

  Scenario: It plays the note it is given
    Given an Easy Synth playing a sine at note 69
    Then the sound repeats 440 times a second

  Scenario: Every knob turned all the way up is still inside full scale
    Given an Easy Synth with every knob but its attack turned all the way up
    Then the sound is heard
    And the sound never goes past full scale

  Scenario: A note let go fades away
    Given an Easy Synth whose note has been let go
    Then the sound has faded to silence
