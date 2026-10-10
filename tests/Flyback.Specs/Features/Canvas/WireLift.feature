Feature: Ctrl+drag takes a wire off an output, one after another
  Ctrl+dragging an output takes one of its wires off it, still plugged in at the far
  end, to be fed from another output. Put back where it came from, it was not the one
  wanted, and the next Ctrl+drag on that output takes the wire after it.

  Scenario: The first Ctrl+drag takes the first wire
    Given the clock feeding a sine and then a saw, with a second clock beside it
    And the patch is open in the editor
    When a wire is Ctrl+dragged off the clock onto the second clock
    Then the sine is fed by the second clock
    And the saw is still fed by the clock

  Scenario: A wire put back passes the next Ctrl+drag on to the wire after it
    Given the clock feeding a sine and then a saw, with a second clock beside it
    And the patch is open in the editor
    When a wire is Ctrl+dragged off the clock and put back
    And a wire is Ctrl+dragged off the clock onto the second clock
    Then the sine is still fed by the clock
    And the saw is fed by the second clock
