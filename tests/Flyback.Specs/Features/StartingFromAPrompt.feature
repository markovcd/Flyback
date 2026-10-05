Feature: A new patch can start from a typed idea
  With an assistant set up, the preset gallery has a card to type an idea in. The
  assistant can write a short idea out as a detailed brief, in place, to read and
  edit. Starting from it puts an empty patch on the canvas and sends the text to
  the assistant, which gets to work at once.

  Scenario: The gallery offers a prompt only when there is an assistant to send it to
    Given no assistant is set up
    When the preset gallery is opened
    Then the gallery has no card to start from a prompt

  Scenario: A short idea is written out in full where it was typed
    Given an assistant is set up
    When the preset gallery is opened
    And "a slow dub track about a night train" is typed in the prompt card
    And the prompt is expanded
    Then the prompt card holds the assistant's detailed brief

  Scenario: Starting from a prompt empties the patch and sends it to the assistant
    Given an assistant is set up
    And a sine driven by Time
    When the preset gallery is opened
    And "a slow dub track about a night train" is typed in the prompt card
    And a patch is started from the prompt
    Then the canvas holds only the Output
    And the assistant's column is open
    And the assistant has been sent "a slow dub track about a night train"
