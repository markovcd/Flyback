Feature: A chord is played from one root note
  A Chord plays a chord picked by number on a root note, as four frequencies
  for four oscillators. An Auto Chord builds the four-note chord a scale has on
  one of its notes, counted in steps from the tonic, so a line of steps plays
  the scale's own chords.

  Scenario: A four-note chord plays its four notes
    Given a Chord on C4 playing maj7
    Then its notes are C4, E4, G4 and B4

  Scenario: A three-note chord adds its root an octave up
    Given a Chord on C4 playing maj
    Then its notes are C4, E4, G4 and C5

  Scenario: A two-note chord adds both of its notes an octave up
    Given a Chord on A3 playing 5th
    Then its notes are A3, E4, A4 and E5

  Scenario: The chord knob names the chord it picks
    Given the chord knob of a Chord from the catalog
    Then at 10 it reads "maj7"

  Scenario: In a major key the chord a step up from the tonic is a minor seventh
    Given an Auto Chord in the Ionian (major) scale of C4, 1 step from the tonic
    Then its notes are D4, F4, A4 and C5

  Scenario: In a harmonic minor key the chord four steps up is a dominant seventh
    Given an Auto Chord in the Harmonic minor scale of A3, 4 steps from the tonic
    Then its notes are E4, G#4, B4 and D5

  Scenario: A step below the tonic counts down the scale
    Given an Auto Chord in the Ionian (major) scale of C4, -1 step from the tonic
    Then its notes are B3, D4, F4 and A4

  Scenario: Seven steps up is the tonic's chord an octave up
    Given an Auto Chord in the Ionian (major) scale of C4, 7 steps from the tonic
    Then its notes are C5, E5, G5 and B5
