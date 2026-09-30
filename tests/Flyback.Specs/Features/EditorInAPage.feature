Feature: The editor in a page offers only what a page can do
  In a browser page the editor opens, saves and records nothing, and has no assistant,
  settings, plugins or About. Its picture stays where the layout puts it, drawn small.

  Scenario: A page's toolbar keeps the patch's buttons and drops the program's
    Given a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    Then the toolbar has none of "open, save, record, assistant, settings, plugins, about"
    And the toolbar still has "undo, redo, tidy, code, controls, swap, side, pause, rewind, view-it"

  Scenario: A page's empty panel names no files, settings or recording, for a mouse or a finger
    Given the clock on its own
    And the editor is in a page
    And the patch is open in the editor
    Then the module panel does not say "Open and Save"
    And the module panel does not say "Settings"
    And the module panel does not say "Record"
    When a finger taps bare canvas
    Then the module panel says "Hold a finger on bare canvas"
    And the module panel does not say "Open and Save"
    And the module panel does not say "Settings"
    And the module panel does not say "Record"

  Scenario: A double-click on a page's picture leaves it where it is
    Given a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    When the picture is double-clicked
    Then the picture does not have the whole window

  Scenario: View it hands the viewer the patch as edited, and pauses the editor behind it
    Given a 220 Hz sine is playing
    And the editor is in a page
    And the patch is open in the editor
    When a script applies the page's text with "freq: 220" changed to "freq: 330"
    And View it is pressed
    Then the viewer is handed the patch under the editor's title, as a bundle whose text has "freq: 330"
    And the patch has stopped

  Scenario: A shared preset the presets page sends opens in the editor under its name
    Given a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    When the page hands the editor the shared preset "Rain" as "rain.fbk"
    Then the editor says it opened "Rain" from the preset site
    And the editor is titled "Rain"

  Scenario: A shared preset needing a plugin the page lacks is not opened, and the page is told why
    Given a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    When the page hands the editor a shared preset needing the "Lantern" plugin
    Then the editor does not open it, and says it needs "Lantern"

  Scenario: A page's picture is drawn small, since it never has the whole window
    Given a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    Then the picture is drawn at 480 x 270
