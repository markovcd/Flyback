Feature: A tutorial checks the patch as it is built
  A tutorial opens on its preset and says one step at a time what to do. After
  every edit it checks the patch against the step: a step done is ticked off and
  the next one shown, and a step not yet done says what is still missing. Whatever
  else is on the canvas is left alone.

  Scenario: A tutorial opens on its preset and asks for the first step
    Given the patch is open in the editor
    When the tutorial "Your first patch" is started
    Then the tutorial says to "Add a Sine"
    And the tutorial shows "Still to do: Add a Sine."

  Scenario: Doing what a step asks ticks it off and shows the next
    Given the patch is open in the editor
    And the tutorial "Your first patch" is started
    When the patch is written as "let wave = sine()"
    Then the tutorial has ticked off "Add a Sine"
    And the tutorial says to "Wire it into the color"
    And the tutorial shows "Still to do: Wire the Sine's out into the Output's color."

  Scenario: A knob off its mark is named with where it is
    Given the patch is open in the editor
    And the tutorial "Your first patch" is started
    When the patch is written as "x |> sine(freq: 2) |> out.color"
    Then the tutorial says to "More stripes"
    And the tutorial shows "Still to do: Turn the Sine's freq to 4; it is at 2."

  Scenario: The command line checks a patch against a tutorial
    Given the text saved as "mine.fbks":
      """
      let wave = sine()
      """
    When flyback-cli tutorial checks "mine.fbks" against "first-patch"
    Then the command says the patch has problems
    And it names "✓ 1. Add a Sine"
    And it names "✗ 2. Wire it into the color  Wire the Sine's out into the Output's color."
