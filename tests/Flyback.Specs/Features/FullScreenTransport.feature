Feature: A full-screen picture keeps its transport behind one set of dots
  Over a picture that has the whole screen, pause, rewind, the seek bar, the loop and
  the sound wait together behind three dots, in the order the toolbar has them. They
  wait at the top and the knobs at the bottom, or the other way round where the
  settings say so, in the editor and the viewer alike.

  Scenario: The editor's full-screen picture keeps its transport at the top
    Given a rainbow across the screen
    And the patch is open in the editor
    When the picture is given the whole window
    Then the transport waits at the top of the picture

  Scenario: The settings can give the top to the knobs
    Given a rainbow across the screen
    And the settings give the top of a full-screen picture to the knobs
    And the patch is open in the editor
    When the picture is given the whole window
    Then the transport waits at the bottom of the picture and the knobs at the top

  Scenario: The viewer keeps its transport at the top
    Given a rainbow across the screen
    When the viewer plays it
    Then the viewer's transport waits at the top of its picture

  Scenario: The viewer can be asked for its transport at the bottom
    Given a rainbow across the screen
    When the viewer plays it with "--transport bottom"
    Then the viewer's transport waits at the bottom of its picture
