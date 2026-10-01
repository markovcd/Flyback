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
