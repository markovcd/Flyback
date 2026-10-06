Feature: A conversation with the assistant counts what it cost
  The assistant's column adds up the tokens each of its turns used, so what a
  patch cost to build can be read off the conversation.

  Scenario: Each turn's tokens are added to the conversation's total
    Given a conversation with the assistant about a patch
    And each turn it takes costs 1000 tokens in, 800 of them cached, and 50 out
    When it is asked twice more
    Then the conversation has cost 2000 tokens in, 1600 of them cached, and 100 out

  Scenario: The conversation names the model that answered
    Given a conversation with the assistant about a patch
    And each turn it takes is answered by claude-opus-5-5
    When it is asked once more
    Then what the conversation cost is told with claude-opus-5-5
