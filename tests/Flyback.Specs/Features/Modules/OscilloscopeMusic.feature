Feature: Oscilloscope music draws on a Beam as it would on an oscilloscope
  Oscilloscope music is made to be watched on an oscilloscope in X-Y mode, its
  left channel across and its right channel up. A Beam patched to the two
  channels draws what the speakers played that way: bright where the beam
  lingers, faint where it jumps, and fading behind itself.

  Scenario: A circle played in stereo is drawn as a ring
    Given a circle played as oscilloscope music onto a Beam, saved as "circle.fbks"
    When flyback-cli draws a still of "circle.fbks" at 1 second
    Then the still is a ring half as wide as the picture is tall, dark inside and out

  Scenario: The Lissajous preset draws a fifth as three loops across and two up
    When flyback-cli saves the preset "Lissajous" as "lissajous.fbks"
    And flyback-cli draws a still of "lissajous.fbks" at 1 second
    Then the still has 3 loops along its top and 2 along its side

  Scenario: Before anything has played the screen is dark
    Given a circle played as oscilloscope music onto a Beam, saved as "circle.fbks"
    When flyback-cli draws a still of "circle.fbks" at 0 seconds
    Then the still is black
