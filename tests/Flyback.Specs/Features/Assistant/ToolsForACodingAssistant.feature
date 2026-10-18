Feature: A coding assistant somebody runs builds a patch with Flyback's own tools
  Somebody's own Claude Code or Codex, with no window and no key of Flyback's, calls
  the tools Flyback's assistant has, one at a time on a patch file. flyback-cli tool
  runs one and writes the patch back, flyback-cli tool history lists what the calls
  did, flyback-cli tool undo takes an edit back and flyback-cli tool redo makes it again.
  flyback-cli mcp serves the same tools to a client that speaks MCP.

  Scenario: A patch built a call at a time reads back as what was built
    When a coding assistant writes a patch of rings into "built.fbks"
    And it adds a sine it calls "lfo"
    And it wires "lfo" into the rings across
    Then "built.fbks" is rings moved across by a sine

  Scenario: Undo takes the last edit back, and redo makes it again
    Given a coding assistant has built rings moved across by a sine in "built.fbks"
    When flyback-cli tool undo is run on "built.fbks"
    Then nothing moves the rings in "built.fbks"
    And the history lists the wire as undone
    When flyback-cli tool redo is run on "built.fbks"
    Then "built.fbks" is rings moved across by a sine

  Scenario: A call the patch cannot take changes nothing and says why
    Given a coding assistant has built rings moved across by a sine in "built.fbks"
    When it edits a piece of text that is not in the patch, "nobody |> out.left"
    Then the call is refused, saying the piece is not in the patch's text
    And "built.fbks" is rings moved across by a sine
    And the history has 3 edits

  Scenario: Editing a patch written as text keeps what its author wrote
    Given "noted.fbks" holds rings under the comment "# turn slowly"
    When a coding assistant turns the rings up to 9 in "noted.fbks"
    Then "noted.fbks" still says "# turn slowly"
    And the rings in "noted.fbks" are at 9

  Scenario: A client that speaks MCP builds a patch, and the file holds it
    When a coding assistant builds rings moved across by a sine in "served.fbks" over flyback-cli mcp
    And it asks what the patch holds, giving the call no arguments
    Then every request it made was answered without an error
    And "served.fbks" is rings moved across by a sine

  Scenario: What a client says over MCP reaches the person
    When a coding assistant says "Built a slow drone in A." over flyback-cli mcp
    Then the person reads "Built a slow drone in A."
