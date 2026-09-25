Feature: The computer keyboard plays in a scale
  Laid out by scale, the computer keyboard plays one of the scales an Auto
  Chord builds in, from a tonic: the home row runs up the scale from the tonic,
  one note a key, so every key is in the key and the same key is always the
  same degree.

  Scenario: The home row runs up the scale from its tonic
    Given the computer keyboard is laid out in D Dorian
    Then the keys A to J play D3, E3, F3, G3, A3, B3 and C4
    And the key K plays nothing

  Scenario: The rows above and below are the same scale an octave away
    Given the computer keyboard is laid out in A Harmonic minor
    Then the keys Q to U play A4, B4, C5, D5, E5, F5 and G#5
    And the keys Z to M play A2, B2, C3, D3, E3, F3 and G#3
