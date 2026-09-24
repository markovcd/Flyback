Feature: A keyboard plays chords across a patch's voices
  A patch with several MIDI Ins is an instrument with that many voices. Each
  note held sounds on a voice of its own, and letting go of one leaves the rest
  playing.

  Scenario: A chord sounds every note on a voice of its own
    Given three voices listening to a keyboard
    When C4, E4 and G4 are held
    Then the voices play C4, E4 and G4

  Scenario: Letting go of one note of a chord stops only its voice
    Given three voices listening to a keyboard
    When C4, E4 and G4 are held
    And E4 is let go
    Then the voices play C4, nothing and G4

  Scenario: A new note takes the voice that came free
    Given three voices listening to a keyboard
    When C4, E4 and G4 are held
    And E4 is let go
    And B4 is played
    Then the voices play C4, B4 and G4

  Scenario: With every voice busy, a new note takes the first voice
    Given three voices listening to a keyboard
    When C4, E4 and G4 are held
    And B4 is played
    Then the voices play B4, E4 and G4

  Scenario: A key held long enough to repeat does not take a second voice
    Given three voices listening to a keyboard
    When C4 is held
    And the keyboard sends C4 again without letting it go
    Then the voices play C4, nothing and nothing

  Scenario: Voices left on automatic share a chord out between them
    Given three voices on automatic listening to a keyboard
    When C4, E4 and G4 are held
    Then the voices play C4, E4 and G4

  Scenario: Letting go of every note leaves every voice silent
    Given three voices listening to a keyboard
    When C4, E4 and G4 are held
    And C4, E4 and G4 are let go
    Then the voices play nothing, nothing and nothing

  Scenario: A single voice falls back to the note still held
    Given one voice listening to a keyboard
    When C4 is held
    And E4 is held
    And E4 is let go
    Then the voice plays C4

  Scenario: A note that lost its voice comes back when the newer note is let go
    Given three voices listening to a keyboard
    When C4, E4 and G4 are held
    And B4 is played
    And B4 is let go
    Then the voices play C4, E4 and G4

  Scenario: A note let go while another sounds over it does not come back
    Given three voices listening to a keyboard
    When C4, E4 and G4 are held
    And B4 is played
    And C4 is let go
    And B4 is let go
    Then the voices play nothing, E4 and G4
