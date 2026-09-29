Feature: The editor in a page offers only what a page can do
  In a browser page the editor opens, saves and records nothing, and has no assistant,
  settings, plugins or About. Its picture stays where the layout puts it, drawn small.

  Scenario: A page's toolbar keeps the patch's buttons and drops the program's
    Given a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    Then the toolbar has none of "open, save, record, assistant, settings, plugins, about"
    And the toolbar still has "undo, redo, tidy, code, controls, swap, side, pause, rewind"

  Scenario: A double-click on a page's picture leaves it where it is
    Given a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    When the picture is double-clicked
    Then the picture does not have the whole window

  Scenario: A shared preset the presets page sends opens in the editor under its name
    Given a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    When the page hands the editor the shared preset "Rain" as "rain.fbk"
    Then the editor says it opened "Rain" from the preset site
    And the editor is titled "Rain"

  Scenario: A page's picture is drawn small, since it never has the whole window
    Given a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    Then the picture is drawn at 480 x 270
