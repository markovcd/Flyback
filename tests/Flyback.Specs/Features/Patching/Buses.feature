Feature: A signal can travel on a bus instead of a wire
  A Send puts what is patched into it on a named bus, and every Receive on that
  bus plays it, wherever it is on the canvas. A kick at one end of a patch can
  key the parts at the other without a wire across it.

  Specified by ADR-0126.

  Scenario: A Receive plays what the Send on its bus carries
    Given a level of 0.3 is sent on the bus "kick"
    And the bus "kick" is received at the speakers
    Then the speakers play 0.3

  Scenario: Any number of parts can listen to one bus
    Given a level of 0.25 is sent on the bus "kick"
    And two parts listening to the bus "kick" are added together at the speakers
    Then the speakers play 0.5

  Scenario: A Receive on a bus nobody sends on is silent, and says so
    Given a level of 0.3 is sent on the bus "kick"
    And the bus "snare" is received at the speakers
    Then the speakers play 0
    And Flyback points out that nothing is sent on "snare"

  Scenario: Putting a Send on another bus takes its listeners with it
    Given a level of 0.3 is sent on the bus "kick"
    And the bus "kick" is received at the speakers
    When the Send is put on the bus "thump"
    Then the speakers play 0.3

  Scenario: A Send that joins a bus somebody else sends on leaves its listeners behind
    Given a level of 0.3 is sent on the bus "kick"
    And the bus "kick" is received at the speakers
    And something else is sent on the bus "snare"
    When the Send is put on the bus "snare"
    Then the speakers play 0
    And Flyback points out that nothing is sent on "kick"

  Scenario: A copy of a Send and its listener is a pair of its own
    Given a level of 0.3 is sent on the bus "kick"
    And the bus "kick" is received at the speakers
    When the Send and its listener are copied and pasted
    Then the copy is on a bus of its own
    And every Send is heard
    And the speakers play 0.3

  Scenario: A listener pasted alone goes on listening to the same bus
    Given a level of 0.3 is sent on the bus "kick"
    And the bus "kick" is received at the speakers
    When the listener alone is copied and pasted
    Then the copy listens to the bus "kick"

  Scenario: A bus can close a loop, which remembers the sample before
    Given a bus that brings back what it carried and adds a quarter
    Then each sample builds on the last: 0.25, 0.5, 0.75
