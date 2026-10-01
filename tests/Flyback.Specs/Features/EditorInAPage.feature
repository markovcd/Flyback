Feature: The editor in a page offers only what a page can do
  In a browser page the editor opens, saves and records nothing, and has no assistant,
  settings, plugins or About. Its picture stays where the layout puts it, drawn small.
  It reaches the preset site it is served from, for shared presets and letters; served
  from anywhere else, it offers neither.

  Scenario: A page's toolbar keeps the patch's buttons and drops the program's
    Given a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    Then the toolbar has none of "open, save, assistant, settings, plugins, about"
    And the toolbar still has "undo, redo, tidy, code, controls, swap, side, transport, view-it"

  Scenario: A page's toolbar leads with the Flyback mark, which goes back to the site
    Given a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    Then the toolbar has "presets-glyph" right after "home"
    When the Flyback mark is pressed
    Then the page leaves for the site's front page

  Scenario: A desktop window's toolbar has no Flyback mark, having no site to go back to
    Given a rainbow across the screen
    And the patch is open in the editor
    Then the toolbar has none of "home"

  Scenario: A page asks before it is left only once the patch is edited
    Given a 220 Hz sine is playing
    And the editor is in a page
    And the patch is open in the editor
    Then the page may be left without asking
    When a script applies the page's text with "freq: 220" changed to "freq: 330"
    Then the page asks before it is left

  Scenario: A page's View it sits beside the presets, where a patch is picked
    Given a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    Then the toolbar has "view-it" right after "presets-glyph"

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

  Scenario: A page opens shared presets from the gallery, but not one needing a plugin it lacks
    Given the preset site shares "Nebula", rated 4.5 by 2 people
    And the preset site shares "Lanterns", which needs the "Lantern" plugin
    And the editor is in a page
    Then the gallery lists "Lanterns" as needing the "Lantern" plugin, and it cannot be picked
    And picking "Nebula" from the gallery opens it

  Scenario: A letter written in a page reaches the preset site
    Given the editor reaches the preset site
    And the editor is in a page
    When a letter saying "The web editor is lovely" is sent from the status bar
    Then the preset site has a letter saying "The web editor is lovely"

  Scenario: A page no preset site serves offers no letter and no shared presets
    Given a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    Then the status bar offers no letter, and no rule before one
    When the preset gallery is opened
    Then the gallery has no preset site section

  Scenario: A page the preset site serves offers a letter
    Given the editor reaches the preset site
    And a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    Then the status bar offers a letter, set apart by a rule

  Scenario: A page's picture is drawn small, since it never has the whole window
    Given a rainbow across the screen
    And the editor is in a page
    And the patch is open in the editor
    Then the picture is drawn at 480 x 270
