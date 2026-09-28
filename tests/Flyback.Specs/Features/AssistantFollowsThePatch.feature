Feature: A conversation with the assistant stays with its patch while the modules and wires do
  Turning a knob, renaming a module or undoing leaves the same patch, so the
  conversation carries on and the assistant is told in a line what changed.
  Adding or taking away a module or a wire makes it another patch, and another
  conversation.

  Scenario: Turning a knob between messages keeps the conversation
    Given a conversation with the assistant about a patch
    When a knob is turned on the canvas
    Then the next message carries on the same conversation
    And the assistant is told the knob's new value

  Scenario: A knob turned while the assistant works survives what it hands back
    Given a conversation with the assistant about a patch
    When the assistant sets one knob while another is turned on the canvas
    Then the patch it hands back has both knobs as they were set

  Scenario: What the assistant was asked to set stands over a knob turned meanwhile
    Given a conversation with the assistant about a patch
    When the assistant sets a knob that is also turned on the canvas while it works
    Then the patch it hands back has that knob as the assistant set it

  Scenario: Adding a wire makes it another conversation
    Given a conversation with the assistant about a patch
    When a wire is added on the canvas
    Then the next message starts a new conversation
