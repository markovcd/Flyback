Feature: Hits hears the drums in a sound
  A Hits splits what it hears into the kick's band, the snare's and the hats', and
  opens a gate on each drum's hits, so music played into a Line In can drive the
  picture. Every band is judged against its own recent level, so how loud the
  music is does not matter.

  Specified by ADR-0239.

  Scenario: Each drum of a loop opens its own gate
    Given a Hits listens to a Line In
    When it hears four seconds of a drum loop at 120 BPM
    Then its kick gate opens 8 times
    And its snare gate opens 4 times
    And its hats gate opens 16 times

  Scenario: A quiet loop is heard the same as a loud one
    Given a Hits listens to a Line In
    When it hears four seconds of a drum loop at 120 BPM, at a thirtieth of the volume
    Then its kick gate opens 8 times
    And its snare gate opens 4 times
    And its hats gate opens 16 times

  Scenario: A held bass note is not a kick
    Given a Hits listens to a Line In
    When it hears a 60 Hz tone held for three seconds
    Then its kick gate opens at most once
