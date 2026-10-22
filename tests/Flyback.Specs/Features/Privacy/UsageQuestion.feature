Feature: The first start asks whether to share usage counts
  A desktop editor that could send anonymous usage counts asks once, at its first
  start, whether to. Nothing is sent before the answer, a yes counts that first start
  too, and a no sends nothing. Builds with nowhere to send counts never ask.

  Scenario: Sharing counts the first start and is not asked again
    Given Flyback could send usage counts and nobody has answered
    When the editor starts
    Then the editor asks whether to share usage counts
    And no usage count has been sent
    When the question is answered "Share counts"
    Then the first start's usage count has been sent
    And the next start does not ask again

  Scenario: Not sharing sends nothing and is not asked again
    Given Flyback could send usage counts and nobody has answered
    When the editor starts
    And the question is answered "Don't share"
    Then no usage count has been sent
    And the next start does not ask again

  Scenario: A build with nowhere to send counts does not ask
    Given Flyback has nowhere to send usage counts
    When the editor starts
    Then the editor does not ask about usage counts
