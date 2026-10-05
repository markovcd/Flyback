Feature: One chain plays a chord down a polyphonic wire
  A MIDI In given several voices plays that many notes at once down one set of
  wires. Every module it reaches plays once per voice, each with a memory of its
  own, until a Merge or the Output adds the voices back into one.

  Scenario: A chord into one MIDI In sounds every note at once
    Given one MIDI In of 3 voices listening to a keyboard, each heard as its pitch while its gate is open
    When C4, E4 and G4 are held
    Then the speakers play C4, E4 and G4 added together

  Scenario: Letting go of one note of the chord stops only that note
    Given one MIDI In of 3 voices listening to a keyboard, each heard as its pitch while its gate is open
    When C4, E4 and G4 are held
    And E4 is let go
    Then the speakers play C4 and G4 added together

  Scenario: A note past the last voice takes the first one
    Given one MIDI In of 2 voices listening to a keyboard, each heard as its pitch while its gate is open
    When C4, E4 and G4 are held
    Then the speakers play G4 and E4 added together

  Scenario: A Merge adds every voice into one
    Given a polyphonic wire of 4 voices numbered from 0, merged into the speakers
    Then the speakers play 6

  Scenario: The speakers add the voices of a wire by themselves
    Given a polyphonic wire of 4 voices numbered from 0, into the speakers
    Then the speakers play 6

  Scenario: The screen adds the voices the same way
    Given a polyphonic wire of 2 voices numbered from 0, on the screen
    Then the screen shows 1
