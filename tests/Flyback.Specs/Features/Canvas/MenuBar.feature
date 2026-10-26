Feature: The menus a Mac shows can be along the top of the window
  On Windows and Linux the editor's menus, the File, Edit, View, Play and Help a Mac
  shows in its menu bar, are along the top of the window once the Canvas settings ask
  for them. An item with a key does what the key does.

  Background:
    Given a level of 0.25 on the screen
    And the patch is open in the editor
    And the editor is on Windows or Linux

  Scenario: The window has no menu bar until the settings ask for one
    Then the window has no menu bar
    When the menu bar is switched on
    Then the menu bar holds File, Edit, View, Play and Help

  Scenario: Undo from the menu bar takes back the last edit
    Given the menu bar is switched on
    When the level is deleted
    And Undo is chosen from the Edit menu
    Then the screen shows 0.25
