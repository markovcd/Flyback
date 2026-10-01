Feature: The knob panel can stand in a fixed grid
  A performer playing from a MIDI controller lays the panel's knobs out the way the
  controller's are, so each one is found in the same row and column as the knob
  under the hand, however wide the window is.

  Background:
    Given the text:
      """
      panel a = 0.1
      panel b = 0.2
      panel c = 0.3
      panel d = 0.4
      panel e = 0.5
      panel f = 0.6
      saw(freq: a(100..400)) |> out.left
      """

  Scenario: Without a grid the knobs run on along one row while there is room
    Given the screen is 1600 pixels wide
    Then the knob panel's rows are "a b c d e f"

  Scenario Outline: Knobs in a grid keep their rows and columns at any width
    Given the screen is <width> pixels wide
    When the knobs are kept in a grid of 4 columns and 2 rows
    Then the knob panel's rows are "a b c d" and "e f"

    Examples:
      | width |
      | 1600  |
      | 500   |

  Scenario: A knob dropped between two slips in there, and one dropped onto another swaps with it
    Given the screen is 1600 pixels wide
    When the knobs are kept in a grid of 4 columns and 2 rows
    And the knob "a" is dropped onto the knob "f"
    Then the knob panel's rows are "f b c d" and "e a"
    When the knob "b" is dropped at the left edge of the knob "e"
    Then the knob panel's rows are "f c d b" and "e a"
