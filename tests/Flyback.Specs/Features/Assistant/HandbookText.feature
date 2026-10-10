Feature: The briefing and the handbook text the assistant reads can each be left out of the conversation
  The assistant is handed a briefing, and looks modules and presets up as it
  works. The conversation shows both, told apart from what it does to the
  patch, and Settings → Assistant hides each on its own.

  Scenario: Looking a module up is reading the handbook
    When the assistant looks up the Filter
    Then what it read is handbook text

  Scenario: Adding a module is not reading the handbook
    When the assistant adds a Filter to the patch
    Then what it did is not handbook text

  Scenario: Both show until they are turned off
    Given the assistant's settings as they first are
    Then the conversation shows the briefing
    And the conversation shows what it looks up

  Scenario: Hiding the briefing leaves the lookups
    Given the assistant's settings as they first are
    When the briefing is turned off and the settings are kept
    Then the conversation does not show the briefing
    And the conversation shows what it looks up

  Scenario: Hiding the lookups leaves the briefing
    Given the assistant's settings as they first are
    When the lookups are turned off and the settings are kept
    Then the conversation does not show what it looks up
    And the conversation shows the briefing
