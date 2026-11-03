Feature: The editor in colors of your choosing
  Settings, Canvas, Colors draws the editor in a color scheme: Graphite, orange on
  charcoal, until another is chosen; Amber on teal-black; Midnight, orange on
  violet-black; Paper, ballpoint blue on cream; Phosphor, the green of the Beam module's
  trace on blue-gray; or one of your own, a file in the schemes folder. A scheme chosen there is the one the editor starts in from then on, and Save offers to
  start Flyback again in it at once.

  Scenario: A scheme chosen in the settings is the one the editor starts in next
    Given a level of 0.25 on the screen
    And the patch is open in the editor
    When Midnight is chosen as the editor's colors
    Then the editor starts in Midnight the next time
    And the editor is drawn in Graphite until then

  Scenario: Flyback offers to start again in the new colors
    Given a level of 0.25 on the screen
    And Flyback can start itself again
    And the patch is open in the editor
    When Midnight is chosen as the editor's colors, leaving the offer to restart ticked
    Then Flyback starts again, in Midnight

  Scenario: A scheme of your own in the schemes folder can be chosen
    Given a color scheme called Sodium in the schemes folder
    And a level of 0.25 on the screen
    And the patch is open in the editor
    When Sodium is chosen as the editor's colors
    Then the editor starts in Sodium the next time
