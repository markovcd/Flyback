Feature: A Path plays a drawing as sound that draws it on a Beam
  A Path reads an SVG, an OBJ model or a PNG's outlines as one closed path and
  goes round it once a cycle at its pitch, across on 'x' and up on 'y'. Played
  to the left and right speakers it is oscilloscope music: a Beam shows the
  drawing, every stroke equally bright. The file is named, not carried.

  Scenario: A square drawn in an SVG draws as a square
    Given the text saved as "square.svg":
      """
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><path d="M0,0 H10 V10 H0 Z"/></svg>
      """
    And "square.svg" traced onto a Beam at half its size, saved as "square.fbks"
    When flyback-cli, with the plugins, draws a still of "square.fbks" at 1 second
    Then the still is lit round a square half as wide as the picture is tall, dark inside and out

  Scenario: A square face of an OBJ model draws round its edges
    Given the text saved as "face.obj":
      """
      v -1 -1 0
      v  1 -1 0
      v  1  1 0
      v -1  1 0
      f 1 2 3 4
      """
    And "face.obj" traced onto a Beam at half its size, saved as "face.fbks"
    When flyback-cli, with the plugins, draws a still of "face.fbks" at 1 second
    Then the still is lit round a square half as wide as the picture is tall, dark inside and out

  Scenario: A drawing that is not there is named
    Given a patch that traces "gone.svg", saved as "gone.fbks"
    When flyback-cli, with the plugins, checks "gone.fbks"
    Then the command says the patch has problems
    And it names "gone.svg"
