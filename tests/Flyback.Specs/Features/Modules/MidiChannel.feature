Feature: A MIDI In can listen to one channel
  A drum machine puts each of its tracks on a MIDI channel of its own. A MIDI In
  given a channel hears that track and no other, so a kick and a hi-hat can each
  play their own part of a patch; one given no channel hears the whole machine.

  Scenario: A note on the module's channel opens its gate
    Given the gate of a MIDI In hearing channel 3 of a drum machine
    When the drum machine plays a note on channel 3
    Then the speakers play 1

  Scenario: A note on another channel leaves it shut
    Given the gate of a MIDI In hearing channel 3 of a drum machine
    When the drum machine plays a note on channel 2
    Then the speakers play 0

  Scenario: Letting the note go closes the gate
    Given the gate of a MIDI In hearing channel 3 of a drum machine
    When the drum machine plays a note on channel 3
    And it plays for 0.01 seconds
    And the drum machine lets the note on channel 3 go
    Then the sound is about 0 at 0.011 seconds
