Feature: A MIDI File plays a MIDI file
  A MIDI File plays the notes of a .mid file as a MIDI In plays a keyboard, on
  the patch's clock, so it sounds the same on the speakers, in an export and
  wherever the patch is played from. The file is named, not carried.

  Scenario: Its gate is high while a note is held
    Given a MIDI File playing a note held for half a second
    Then the sound is about 1 at 0.25 seconds
    And the sound is about 0 at 0.75 seconds

  Scenario: A voice plays one of the notes held at once
    Given a MIDI File on voice 2 playing two notes held together for a second
    Then the sound is about 1 at 0.5 seconds

  Scenario: A voice with no note to play stays silent
    Given a MIDI File on voice 3 playing two notes held together for a second
    Then the sound is about 0 at 0.5 seconds

  Scenario: A file that is not there is named
    Given a MIDI File playing a file that is not there
    Then the patch complains that the MIDI file cannot be read
