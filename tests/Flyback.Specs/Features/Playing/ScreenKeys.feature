Feature: A MIDI In is played from keys on the screen
  With no controller plugged in and no computer keyboard under the hand, as on a phone or
  a tablet, a MIDI In is played from a row of keys along the foot of the window: an octave
  of a piano, or the home row of the patch's scale, moved an octave at a time with two
  buttons. A finger brings the row up by itself; the toolbar's keys button, there only while a MIDI In
  listens, does elsewhere.

  Scenario: A finger on a patch played from the keyboard brings the keys up
    Given a MIDI In played from the computer keyboard, its pitch on the screen
    And the patch is open in the editor
    When a finger taps the canvas
    Then the keys on the screen are up

  Scenario: A key on the screen plays its note until it is let go
    Given a MIDI In played from the computer keyboard, its pitch on the screen
    And the patch is open in the editor
    When the keys button is pressed in
    And a finger presses the "C3" key on the screen
    Then the MIDI In plays C3
    When the finger lifts
    Then the MIDI In is let go

  Scenario: The octave buttons move the keys
    Given a MIDI In played from the computer keyboard, its pitch on the screen
    And the patch is open in the editor
    When the keys button is pressed in
    And the octave up button is pressed
    Then the keys on the screen run from C4

  Scenario: The keys run up the patch's scale from its tonic
    Given a MIDI In played from the computer keyboard, its pitch on the screen
    And the patch lays the computer keyboard out in D Dorian
    And the patch is open in the editor
    When the keys button is pressed in
    Then the keys on the screen play D3, E3, F3, G3, A3, B3 and C4

  Scenario: The keys are offered only while a MIDI In listens to the keyboard
    Given the clock on its own
    And the patch is open in the editor
    Then the toolbar has no keys button
