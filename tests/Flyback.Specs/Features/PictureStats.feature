Feature: A full-screen picture can say how it is being drawn
  Somebody tuning a heavy patch for a screening wants to know whether it keeps up,
  without leaving the picture: F3 puts a line in its corner saying how many frames a
  second it draws and what each one costs, and F3 again takes it away.

  Scenario: The viewer shows how it draws when asked on the command line
    Given a rainbow across the screen
    When the viewer plays it with "--stats"
    Then the viewer's picture says how many frames a second it draws

  Scenario: The viewer keeps the picture clean unless asked
    Given a rainbow across the screen
    When the viewer plays it
    Then the viewer's picture says nothing about how it is drawn

  Scenario: F3 over the viewer's picture shows how it draws, and F3 again hides it
    Given a rainbow across the screen
    When the viewer plays it
    And F3 is pressed over the viewer's picture
    Then the viewer's picture says how many frames a second it draws
    When F3 is pressed over the viewer's picture
    Then the viewer's picture says nothing about how it is drawn

  Scenario: F3 over the editor's full-screen picture shows how it draws
    Given a rainbow across the screen
    And the patch is open in the editor
    When the picture is given the whole window
    And F3 is pressed
    Then the editor's picture says how many frames a second it draws
