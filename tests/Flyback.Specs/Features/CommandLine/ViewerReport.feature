Feature: The viewer reports how a run went
  Somebody measuring a patch from a script, with no one watching the window, wants
  the run to say what it held: what drew the picture, how many frames a second, and
  how slow the slowest frame was. flyback-viewer --report prints that when the run ends.

  Scenario: The report says what drew the picture and what it held
    Given a rainbow across the screen
    When the viewer plays it with "--report --cpu"
    Then the viewer's report says the picture was drawn by "CPU"
    And the viewer's report gives the frames a second and the slowest frame

  Scenario: The report says there was no sound for a patch that makes none
    Given a rainbow across the screen
    When the viewer plays it with "--report"
    Then the viewer's report says there is no sound
