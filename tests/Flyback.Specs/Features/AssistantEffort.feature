Feature: The effort setting can only be changed where it is sent
  Effort is how hard the assistant thinks before it answers. Where an assistant
  cannot send it, the setting is grayed out and says why, rather than being a
  choice that changes nothing.

  Scenario: An assistant that never sends effort grays the setting out
    When the OpenAI-compatible assistant's settings are opened
    Then the effort setting cannot be changed
    And the effort setting says why

  Scenario: Gemini grays the setting out for a model nobody has probed
    When the Gemini assistant's settings are opened
    Then the effort setting cannot be changed
    And the effort setting says why

  Scenario: Gemini sends effort once a probe has measured the model's thinking
    Given a probe has measured how long gemini-3.6-flash thinks
    When the Gemini assistant's settings are opened
    Then the effort setting can be changed

  Scenario: Claude Code always sends effort
    When the Claude Code assistant's settings are opened
    Then the effort setting can be changed
