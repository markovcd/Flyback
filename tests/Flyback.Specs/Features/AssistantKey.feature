Feature: The assistant's key goes only where it has to
  A key that is sent can be read on the way or kept by whatever it reached. So an
  admin key is never sent at all, an exported key does not cross the network
  unencrypted, and a message with the key in it goes nowhere.

  Scenario: An admin key is never sent
    Given an admin key entered for an assistant that sends to https://api.example.test
    When the assistant sends a request there
    Then the request carries no key

  Scenario: An exported key does not cross the network unencrypted
    Given a key exported for an assistant that sends to http://192.0.2.10:11434
    When the assistant sends a request there
    Then the request carries no key
    And the assistant says the key is not sent over plain http

  Scenario: An exported key still reaches a server on this machine
    Given a key exported for an assistant that sends to http://localhost:11434
    When the assistant sends a request there
    Then the request carries the key

  Scenario: A message with the key in it is not sent
    Given a conversation with an assistant that has a key
    When a message with the key in it is sent
    Then the assistant never hears it
    And the person is told the message had the key in it
