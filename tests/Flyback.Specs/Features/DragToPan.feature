Feature: Drag to pan
  With drag to pan switched on in the canvas settings, the left button drags empty
  canvas to move the view, for a mouse or trackpad with no middle button. The right
  button then selects: dragged across modules it takes them, and clicked it opens
  the module list. A finger keeps its own gestures.

  Specified by ADR-0182.

  Background:
    Given a Filter
    And drag to pan is switched on

  Scenario: Dragging empty canvas moves the view and selects nothing
    When empty canvas beside the Filter is dragged
    Then the view follows the drag
    And nothing is selected

  Scenario: Right-dragging across a module selects it
    When the right button is dragged across the Filter
    Then the Filter is selected
    And the view stays put
