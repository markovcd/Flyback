Feature: A new patch can start from a typed idea
  With an assistant set up, the preset gallery has a card to type an idea in. The
  Starting from it puts an empty patch on the canvas, has the assistant write the
  idea out as a detailed brief and sends it, since a patch built from a bare idea
  comes out thin. A different model may do the writing out.

  Scenario: The gallery offers a prompt only when there is an assistant to send it to
    Given no assistant is set up
    When the preset gallery is opened
    Then the gallery has no card to start from a prompt

  Scenario: Starting from an idea writes it out first and sends the brief
    Given an assistant is set up
    And a sine driven by Time
    When the preset gallery is opened
    And "a slow dub track about a night train" is typed in the prompt card
    And a patch is started from the prompt
    Then the canvas holds only the Output
    And the assistant's column is open
    And the transcript says the idea was written out first
    And the assistant was asked to write the idea out once
    And the assistant has been sent the brief

  Scenario: An idea is written out by the model chosen for ideas
    Given an assistant is set up
    And ideas are written out by "sketcher"
    When the preset gallery is opened
    And "a slow dub track about a night train" is typed in the prompt card
    And a patch is started from the prompt
    Then the idea was written out by "sketcher" and the patch built by "briefing"
