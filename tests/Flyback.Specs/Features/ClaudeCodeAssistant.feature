Feature: Claude Code is an assistant that needs no key
  Someone signed in to Claude Code can have the assistant build their patches
  without making an API key: Flyback runs the program they installed, and their
  plan pays.

  Scenario: Claude Code is ready to use as soon as it is installed
    Given Claude Code is installed
    When the assistants are listed
    Then Claude Code is among them
    And Claude Code is available
    And Claude Code asks for no key
