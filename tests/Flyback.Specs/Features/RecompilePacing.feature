Feature: A knob dragged in the web editor recompiles at a pace the page keeps up with
  A page has one thread for the canvas, the preview and the compile, and a drag asks
  for a compile at every step. The page compiles the first step at once, then at most
  once every tenth of a second or twice as long as the last compile took, and always
  the step the drag stopped on. The desktop compiles every step, on threads of its own.

  Scenario: A knob dragged for a second compiles ten times and lands where it stopped
    Given the web editor compiles a patch in 10 milliseconds
    When a knob is dragged for 1 second, sixty steps a second
    Then the patch compiled about ten times during the drag
    And once more after it, at the step it stopped on
