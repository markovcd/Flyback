Feature: An envelope shapes a note from its gate
  While the gate is open the envelope rises to full over its attack, falls to
  its sustain level over its decay and holds there. When the gate closes it
  fades to nothing over its release.

  Background:
    Given an envelope with a 10 ms attack, 100 ms decay, sustain of 0.5 and 100 ms release, held for half a second

  Scenario: A held note rises to full over its attack
    Then the sound is about 1 at 0.01 seconds

  Scenario: A held note settles at its sustain level
    Then the sound is about 0.5 at 0.4 seconds

  Scenario: Letting go fades the note out over its release
    Then the sound is about 0 at 0.65 seconds
