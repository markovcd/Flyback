Feature: A tone changes pitch without clicking
  An oscillator keeps track of how far round it has turned, so a new frequency
  bends the wave from where it stands rather than jumping it somewhere else.

  Specified by ADR-0030, and by ADR-0048 for the clock it turns against.

  Scenario: A tone keeps time in seconds
    Given a 1 Hz sine is playing
    Then the sound is about 1 at 0.25 seconds
    And the sound is about 0 at 0.5 seconds
    And the sound is about -1 at 0.75 seconds

  Scenario: A frequency that jumps bends the wave rather than breaking it
    Given a sine whose frequency jumps from 4 Hz to 12 Hz at 0.15 seconds
    When it plays for 0.3 seconds
    Then the sound never clicks
