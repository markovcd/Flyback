Feature: A trackpad on a Mac moves and zooms the view the Mac way
  On a Mac, two fingers scrolling on the trackpad move the view and a pinch zooms
  it, as in every other Mac program. Ctrl or Cmd with a scroll zooms, for a mouse.
  Elsewhere the wheel zooms, as it always has.

  Specified by ADR-0217.

  Background:
    Given a Filter
    And the editor runs on a Mac

  Scenario: Two fingers scrolling move the view and leave the zoom
    When two fingers scroll on the trackpad
    Then the view has scrolled with them
    And the zoom is as it was

  Scenario: Spreading two fingers zooms in
    When two fingers spread on the trackpad
    Then the view is zoomed in

  Scenario: Cmd and the wheel zoom in, for a mouse
    When the wheel turns with Cmd held
    Then the view is zoomed in
