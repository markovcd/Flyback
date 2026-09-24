Feature: Fractals: one point c, mapped, pictured and heard
  The Fractals plugin, which the preset site starts with, has three modules
  about one point c. Mandelbrot maps every c in the gradient everybody knows it
  by, with the set itself black. Julia draws the picture of one c. Orbit plays
  one c, stepping z to z squared plus c from nought, or in Julia mode from one
  pixel of the Julia set, so an orbit that settles into a cycle is a tone.

  Scenario: The Mandelbrot set is black and the plane around it is colored
    Given the Mandelbrot set on the screen
    Then the point -0.2, 0 of the plane is black
    And the point -2.5, 0 of the plane is blue

  Scenario: Shifting the colors leaves the set black
    Given the Mandelbrot set on the screen with its colors shifted by 0.5
    Then the point -0.2, 0 of the plane is black
    And the point -2.5, 0 of the plane is not blue

  Scenario: A Julia set's middle is black exactly when its c is in the Mandelbrot set
    Given the Julia set of -1, 0 on the screen
    Then the middle of the screen is black
    Given the Julia set of 0.5, 0 on the screen
    Then the middle of the screen is not black

  Scenario: An orbit that settles into a cycle of three is a tone at a third of its rate
    Given the orbit of -0.12, 0.75 stepping 330 times a second
    Then the sound repeats 110 times a second

  # With c at nought, 1.5 squares to 2.25, which has escaped, and is back at 1.5.
  Scenario: A Julia orbit that escapes starts again from its pixel
    Given the Julia orbit of 0, 0 from the pixel 1.5, 0 stepping 300 times a second
    Then the sound repeats 150 times a second
