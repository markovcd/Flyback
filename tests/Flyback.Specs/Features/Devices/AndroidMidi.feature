Feature: A keyboard plugged into an Android device plays a MIDI In
  On a phone or a tablet, Android hands over what a USB keyboard sends as one stream of
  bytes rather than a message at a time: a note may arrive in two pieces, a run of notes
  may share one status byte, and a clock tick may land in the middle of a note. Every
  note is heard whole all the same.

  Scenario: A note that arrives in two pieces is heard once, whole
    Given a keyboard whose notes arrive as a stream of bytes
    When it sends C4 pressed, cut in two
    Then C4 is heard pressed once

  Scenario: Notes that share one status byte are each heard
    Given a keyboard whose notes arrive as a stream of bytes
    When it sends C4, E4 and G4 pressed under one status byte
    Then C4, E4 and G4 are heard pressed

  Scenario: A clock tick in the middle of a note disturbs neither
    Given a keyboard whose notes arrive as a stream of bytes
    When it sends C4 pressed with a clock tick between its bytes
    Then a tick and C4 pressed are heard
