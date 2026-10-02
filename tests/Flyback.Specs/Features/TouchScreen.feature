Feature: A hand on a touch screen patches without a mouse or keys
  One finger is the left button, a finger held still is the right, and two fingers
  move and zoom the view. A fingertip that lands beside a socket lands on it.

  Specified by ADR-0165.

  Background:
    Given the clock on its own

  Scenario: Two fingers spread apart zoom the canvas in
    Given the patch is open in the editor
    When two fingers spread apart on the canvas
    Then the canvas is drawn larger
    And the clock has not moved

  Scenario: Two fingers dragged together move the view, not the patch
    Given the patch is open in the editor
    When two fingers drag across the canvas
    Then the view has moved
    And the clock has not moved

  Scenario: A finger held on bare canvas opens the list of modules there
    Given the patch is open in the editor
    When a finger is held on bare canvas
    And "Sine" is picked from the list that opens
    Then a sine stands where the finger was held

  Scenario: The list a finger opens leaves the on-screen keyboard down until its filter is tapped
    Given the patch is open in the editor
    When a finger is held on bare canvas
    Then the list's filter box waits to be tapped

  Scenario: The list a mouse opens is ready to type into
    Given the patch is open in the editor
    When bare canvas is right-clicked
    Then the list's filter box takes what is typed

  Scenario: The empty panel says what a finger does, and still where the files and settings are
    Given the patch is open in the editor
    When a finger taps bare canvas
    Then the module panel says "Hold a finger on bare canvas"
    And the module panel says "Open and Save are on the toolbar."
    And the module panel does not say "Ctrl+O"

  Scenario: A finger copies and pastes a module with buttons, with no keys
    Given the patch is open in the editor
    When a finger taps the clock
    And the "Copy" button is pressed
    And the "Paste" button is pressed
    Then there are two clocks

  Scenario: A finger selects every module with a button
    Given a sine beside the clock
    And the patch is open in the editor
    When the "Select all" button is pressed
    Then every module is selected

  Scenario: A finger picks a module out from between its sockets with the view all the way out
    Given a sine beside the clock
    And the patch is open in the editor
    When the view is zoomed all the way out
    And a finger taps the sine a third of the way in, level with its first input
    Then the sine is selected

  Scenario: A wire drawn by a finger lands on the socket it ends beside
    Given a sine beside the clock
    And the patch is open in the editor
    When a finger draws a wire from the clock to just short of the sine's first input
    Then a sine is fed by the clock
