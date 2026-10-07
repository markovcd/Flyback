Feature: The assistant can be asked from the command line
  flyback-cli ask talks to the assistant the editor is set to, about a patch
  file, and writes its answer back into that file with the conversation, so a
  script, an agent or a person at a terminal can carry it on, and so can the
  editor.

  Scenario: The assistant's answer is written into the patch file
    Given an assistant that builds a gray field when asked
    When flyback-cli asks it about "field.fbk" for "a gray field"
    Then "field.fbk" shows a gray field

  Scenario: Asking about the same file again carries the conversation on
    Given an assistant that builds a gray field when asked
    When flyback-cli asks it about "field.fbk" for "a gray field"
    And flyback-cli asks it about "field.fbk" for "now brighter"
    Then the assistant remembers being asked for "a gray field"

  Scenario: The editor carries on a conversation the command line had
    Given an assistant that builds a gray field when asked
    When flyback-cli asks it about "field.fbkb" for "a gray field"
    Then opening "field.fbkb" in the editor carries the conversation on

  Scenario: Each turn ends by saying what it cost
    Given an assistant that builds a gray field when asked
    When flyback-cli asks it about "field.fbk" for "a gray field" as JSON
    Then the turn's last line counts its requests and the tokens they took

  Scenario: A question is answered rather than built when a decision model reads it as one
    Given an assistant that builds a gray field when asked
    And a decision model that reads every message as a question about the patch
    When flyback-cli asks it about "field.fbk" for "why is it gray?"
    Then the assistant was told to answer rather than build

  Scenario: A flag ask does not have is refused rather than sent to the assistant
    Given an assistant that builds a gray field when asked
    When flyback-cli asks it about "field.fbk" with "--turns 1" after the patch
    Then the command is refused, naming "--turns"
    And the assistant was asked nothing

  Scenario: A short idea is written out in full and nothing is built
    Given an assistant that writes ideas out in full
    When flyback-cli expands "a slow tide" over "idea.fbk"
    Then the brief is printed
    And "idea.fbk" does not exist
