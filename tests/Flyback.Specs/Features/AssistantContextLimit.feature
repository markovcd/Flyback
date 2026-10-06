Feature: A conversation with the assistant ends when it has grown too large
  Every request carries the whole conversation, so what bounds one is how large
  it has grown, set under Settings → Assistant, not how many messages it has had.

  Scenario: A small conversation carries on however many messages it has had
    Given a conversation with the assistant limited to 50000 tokens of context
    And each turn it takes costs 1000 tokens in, 800 of them cached, and 50 out
    When it is asked 20 times more
    Then it still takes the next message

  Scenario: A conversation that has grown past its limit takes no more messages
    Given a conversation with the assistant limited to 50000 tokens of context
    And each turn it takes costs 60000 tokens in, 0 of them cached, and 50 out
    When it is asked once more
    Then it takes no more messages, saying it has grown past its limit
