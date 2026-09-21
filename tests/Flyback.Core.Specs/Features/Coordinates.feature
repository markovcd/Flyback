Feature: The picture means the same at any size and shape
  Every module that touches space or color agrees on what its numbers mean: y
  runs from -1 at the bottom to 1 at the top, x is the same scale widened by the
  aspect ratio, and 0 to 1 is black to full with nothing applied on the way out.

  Specified by ADR-0014.

  # The preview is honest about what a 1920x1080 export will look like. Compared
  # where the two grids' pixel centers coincide, so it is an equality.
  Scenario: The same patch at two sizes is the same picture
    Given rings drawn across the screen
    Then the picture at 96 by 54 matches the picture at 32 by 18

  # sin(y) curves the way it does on paper, and Rotate turns anticlockwise for a
  # positive angle.
  Scenario: Up is up
    Given a brightness that follows height on the screen
    Then the picture gets brighter towards the top

  Scenario: A circle stays a circle on a screen that is not square
    Given a disc of radius one half on the screen
    Then the disc is round at 320 by 180
    And the disc is round at 96 by 54

  # No gamma: the number on a node predicts the pixel. sRGB would put this at 188.
  Scenario: A level of one half is the middle byte
    Given a level of 0.5 on the screen
    Then each channel is stored as 128

  # No headroom to pull back later. That is also what keeps a feedback loop with
  # gain above one from running away.
  Scenario: Anything above one is white
    Given a level of 4 on the screen
    Then each channel is stored as 255

  Scenario: Anything below zero is black
    Given a level of -1 on the screen
    Then each channel is stored as 0
    And the screen is black
