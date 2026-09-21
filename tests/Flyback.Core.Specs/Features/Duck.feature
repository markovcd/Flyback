Feature: A part can make room for a kick
  A Duck turns a part down while its key is loud, and brings it back once the
  key has gone. The key may be the kick's sound or its envelope.

  Specified by ADR-0125.

  Scenario: The kick takes the pad down by exactly the depth it is set to
    Given a pad at 0.5 ducked by 0.6 under a kick at full level
    When it plays for 0.1 seconds
    Then that sample is about 0.2

  Scenario: The pad comes back once the kick has gone
    Given a pad at 0.5 ducked by 0.6 under a kick that stops after 0.1 seconds
    When it plays for 1 seconds
    Then that sample is about 0.5
