Feature: Codex is an assistant that needs no key
  Someone signed in to Codex can have the assistant build their patches
  without making an API key: Flyback runs the program they installed, and their
  plan pays.

  Scenario: Codex is ready to use as soon as it is installed
    Given Codex is installed
    When Codex is looked for among the assistants
    Then Codex is among them
    And Codex is available
    And Codex asks for no key
