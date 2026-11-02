Feature: The editor in colors of your choosing
  Settings, Canvas, Colors draws the editor in a color scheme: Phosphor, the green of
  the Beam module's trace on blue-gray; Amber on teal-black; Graphite, orange on
  charcoal; Midnight, orange on violet-black; Paper, ballpoint blue on cream; or one of
  your own, a file in the schemes folder. A scheme chosen there is the one the editor starts in from then on.

  Scenario: A scheme chosen in the settings is the one the editor starts in next
    Given a level of 0.25 on the screen
    And the patch is open in the editor
    When Midnight is chosen as the editor's colors
    Then the editor starts in Midnight the next time
    And the editor is drawn in Phosphor until then

  Scenario: A scheme of your own in the schemes folder can be chosen
    Given a color scheme called Sodium in the schemes folder
    And a level of 0.25 on the screen
    And the patch is open in the editor
    When Sodium is chosen as the editor's colors
    Then the editor starts in Sodium the next time
