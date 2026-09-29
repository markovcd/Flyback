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

  Scenario: A wire drawn by a finger lands on the socket it ends beside
    Given a sine beside the clock
    And the patch is open in the editor
    When a finger draws a wire from the clock to just short of the sine's first input
    Then a sine is fed by the clock
