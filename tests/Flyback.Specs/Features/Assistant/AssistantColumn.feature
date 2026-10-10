Feature: The assistant's column reads as a conversation
  The person's messages stand on the right, in a bubble of their own, and the
  assistant's words, folded steps and proposals follow under its mark. A message
  too long to read past stands cut short until it is opened. The header names
  the model, the one set up until one has answered. Under the column's
  header a line says how full the conversation's context is, and opens onto what
  the conversation has cost. What the assistant writes in Markdown is drawn as
  such rather than shown as typed.

  Scenario: A long message stands cut short until it is opened
    Given a message of 20 lines was sent to the assistant
    Then the message stands cut short, offering "Show all"
    When "Show all" is pressed
    Then the whole message stands open, offering "Show less"

  Scenario: The header names the model set up before anything has answered
    Given an assistant is set up
    And the assistant's model is "briefing-large"
    When the assistant's column is opened
    Then the column's header names "briefing-large"

  Scenario: The column says how full the context is, and opens onto the cost
    Given a conversation that sent 87040 tokens, 80000 of them cached, wrote 3100, and last sent 45000
    When the patch it was saved with is opened beside the assistant
    Then the column shows "45k / 100k" of context
    When the context line is pressed
    Then the column shows "87k" in, "80k" cached, "3.1k" out and "1" turn

  Scenario: The assistant's Markdown is drawn rather than shown as typed
    Given the assistant replied
      """
      **Verdict:** about right.
      - `level` sits at 0.8
      - nothing clips
      """
    Then "Verdict:" is drawn in bold
    And "level" is drawn as code
    And the reply reads "Verdict: about right.", "•  level sits at 0.8" and "•  nothing clips"
