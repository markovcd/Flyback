Feature: A coding assistant somebody runs edits the patch open in the editor
  With Settings → Assistant → "Let a program on this computer edit the open patch" on, the editor listens on a
  channel only the same user can reach, and flyback-cli mcp --editor runs each tool call
  on a copy of the patch open in it, as the assistant's own column does. Only a proposal
  reaches the canvas, as one edit the person can undo, and the column shows what the
  program did and what it said, with no box to type in while it drives.

  Scenario: An edit over MCP waits for a proposal, which puts it on the canvas, and undo takes it back
    Given the editor lets a program on this computer edit the open patch
    When "Claude Code" writes a sine into the open patch over flyback-cli mcp --editor
    Then the call was answered without an error
    And the canvas holds no sine
    When "Claude Code" proposes "A sine on the left." over flyback-cli mcp --editor
    Then the call was answered without an error
    And the canvas holds a sine
    And the editor keeps letting programs edit it, next time it starts
    When that is undone
    Then the canvas holds no sine

  Scenario: After a proposal on a drawn patch, the program goes on editing the text it wrote
    Given the editor lets a program on this computer edit the open patch
    When "Claude Code" writes this into the open patch over flyback-cli mcp --editor:
      """
      // Parts: 1 kick, 2 hat
      let kick = t |> sine(freq: 55)
      let hiss = noise()
      let hat = hiss.white
      mixer(kick, 0.5, hat, 0.25) |> remap(0..1, 45..260) |> out.left
      """
    And "Claude Code" proposes "A kick and a hat." over flyback-cli mcp --editor
    And "Claude Code" reads the open patch over flyback-cli mcp --editor
    Then the answer has the line "// Parts: 1 kick, 2 hat"
    And the answer has the line "let hat = hiss.white"
    And the answer has the line "mixer(kick, 0.5, hat, 0.25) |> remap(0..1, 45..260) |> out.left"

  Scenario: Until somebody lets it, no program can edit the open patch
    Given the editor has not been let edit by a program
    When "Claude Code" writes a sine into the open patch over flyback-cli mcp --editor
    Then the call is refused, saying to turn on "Let a program on this computer edit the open patch"
    And the canvas holds no sine

  Scenario: What a program says over MCP shows on the editor's status line
    Given the editor lets a program on this computer edit the open patch
    When "Claude Code" says "Built a slow drone in A." to the editor over flyback-cli mcp --editor
    Then the call was answered without an error
    And the editor's status line says "Built a slow drone in A."

  Scenario: The column shows a program's calls while it drives, and gives the box back after
    Given the assistant's column is open
    And the editor lets a program on this computer edit the open patch
    When "Claude Code" writes a sine into the open patch over flyback-cli mcp --editor
    Then the column says it is driven by "Claude Code"
    And the column shows the call, and has no box to type in
    When "Claude Code" says "Added a sine." to the editor over flyback-cli mcp --editor
    Then the column shows "Added a sine." as what was said
    When the program closes its end
    Then the column says "Claude Code" stopped driving, and has its box back
