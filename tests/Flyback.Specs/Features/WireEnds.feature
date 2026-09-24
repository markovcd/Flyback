Feature: The panel says where each wire goes
  A patched socket's row in the module panel names the socket at the other end
  of its wire, the module by the name the canvas draws it with, so a wire can be
  followed without finding it on the canvas.

  Scenario: A patched input names the socket it is patched from
    Given a sine driven by Time
    Then the sine's "freq" reads "◀ patched from Time.t"

  Scenario: A patched output names every socket it feeds
    Given a sine driven by Time
    And Time also drives the sine's "phase"
    Then Time's "t" reads "▶ patched to Sine.freq, Sine.phase"

  Scenario: A renamed module is named by its new name
    Given a sine driven by Time
    When Time is renamed "beat"
    Then the sine's "freq" reads "◀ patched from beat.t"

  Scenario: A socket on a box names the far end of its wire as the module's row does
    Given a sine driven by Time
    And the sine is drawn in one box with a Multiply it feeds
    Then the box's "Sine.freq" reads "◀ patched from Time.t"
