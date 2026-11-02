Feature: The editor in colors of your choosing
  Settings, Canvas, Colors draws the editor in one of four schemes: Phosphor, the
  green of the Beam module's trace on blue-gray; Amber on teal-black; Graphite, orange
  on charcoal; and Midnight, orange on violet-black. A scheme chosen there is the one
  the editor starts in from then on.

  Background:
    Given a level of 0.25 on the screen
    And the patch is open in the editor

  Scenario: A scheme chosen in the settings is the one the editor starts in next
    When Midnight is chosen as the editor's colors
    Then the editor starts in Midnight the next time
    And the editor is drawn in Phosphor until then
