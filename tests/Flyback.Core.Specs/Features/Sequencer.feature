Feature: A sequencer steps through its values in time
  A sequencer plays a list of values one after another at a steady rate, and
  opens a gate at the start of each step for an envelope to follow.

  Specified by ADR-0031 and ADR-0038.

  Scenario: Each value plays for one step
    Given a sequencer of 0.2, 0.4, 0.6, 0.8 stepping 4 times a second
    Then the sound is about 0.2 at 0.1 seconds
    And the sound is about 0.4 at 0.35 seconds
    And the sound is about 0.6 at 0.6 seconds
    And the sound is about 0.8 at 0.85 seconds

  Scenario: After the last value it starts again from the first
    Given a sequencer of 0.2, 0.4, 0.6, 0.8 stepping 4 times a second
    Then the sound is about 0.2 at 1.1 seconds

  Scenario: The gate is open for the first half of each step
    Given the gate of a sequencer stepping 4 times a second
    Then the sound is about 1 at 0.05 seconds
    And the sound is about 0 at 0.2 seconds
    And the sound is about 1 at 0.3 seconds
