Feature: What the numbers on a patch mean
  Every module that touches space or colour has to agree on what its numbers
  mean. Three conventions fix that: y runs -1 to 1 bottom to top, x is the same
  scale widened by the aspect ratio, and an output of 0..1 is what a channel
  gets with nothing applied on the way out.

  A frame is a sampling of a patch rather than the patch itself, so these
  scenarios ask about points on the picture rather than about pixels — which is
  what lets the same question be put to two frames of different sizes.

  Specified by ADR-0014.

  # The resolution-independence claim, and the one that makes the preview honest
  # about what a 1920x1080 export will look like. The two frames are compared at
  # the points where their grids coincide exactly, so this is an equality rather
  # than a likeness — and because pixel centres are what coincide, it pins the
  # half-pixel offset as well.
  Scenario: The same patch at two resolutions is the same picture
    Given a patch containing:
      | name   | module         |
      | coords | coord          |
      | rings  | pattern.rings  |
      | screen | video.output   |
    And "coords" output "x" is wired to "rings" input "x"
    And "coords" output "y" is wired to "rings" input "y"
    And "rings" input "freq" is set to 3
    And "rings" output "out" is wired to "screen" input "colour"
    When the patch is compiled
    Then compilation reports no issues
    And the frame at 96 by 54 matches the frame at 32 by 18

  # Screen rows count downwards and patch y counts upwards. The single inversion
  # lives in the renderer's row loop, so sin(y) curves the way it does on paper
  # and Rotate turns anticlockwise for a positive angle.
  Scenario: y runs bottom to top, not top to bottom
    Given a patch containing:
      | name   | module       |
      | coords | coord        |
      | spread | math.remap   |
      | screen | video.output |
    And "coords" output "y" is wired to "spread" input "in"
    And "spread" output "out" is wired to "screen" input "colour"
    When the patch is compiled
    Then compilation reports no issues
    And the frame gets brighter towards the top

  # x spans -aspect..aspect rather than -1..1, so Length(x, y) is a true radius
  # and everything radial behaves on a window of any shape. Normalising both
  # axes to -1..1 instead would make this disc an ellipse as wide as the frame.
  Scenario: A circle stays a circle on a frame that is not square
    Given a patch containing:
      | name   | module       |
      | coords | coord        |
      | edge   | math.step    |
      | screen | video.output |
    And "coords" output "radius" is wired to "edge" input "in"
    And "edge" input "edge" is set to 0.5
    And "edge" output "out" is wired to "screen" input "colour"
    When the patch is compiled
    Then compilation reports no issues
    And a circle is as wide as it is tall at 320 by 180
    And a circle is as wide as it is tall at 96 by 54

  # No gamma is applied, deliberately: what a module computes is what the pixel
  # gets, so the number on a node predicts the output. sRGB encoding would be
  # more correct for physical light and would put this pixel at 188.
  Scenario: A value of one half is the byte in the middle
    Given a patch containing:
      | name   | module       |
      | knob   | value        |
      | screen | video.output |
    And "knob" input "value" is set to 0.5
    And "knob" output "out" is wired to "screen" input "colour"
    When the patch is compiled
    Then the centre pixel is byte 128

  # There is no headroom to pull back down with a later Gain: a patch computing
  # 4 sees the same white as one computing 1. That is also what stops a feedback
  # loop with gain above 1 diverging, so the two decisions are linked.
  Scenario: Anything above one is white rather than headroom
    Given a patch containing:
      | name   | module       |
      | knob   | value        |
      | screen | video.output |
    And "knob" input "value" is set to 4
    And "knob" output "out" is wired to "screen" input "colour"
    When the patch is compiled
    Then the centre pixel is byte 255

  Scenario: Anything below zero is black
    Given a patch containing:
      | name   | module       |
      | knob   | value        |
      | screen | video.output |
    And "knob" input "value" is set to -1
    And "knob" output "out" is wired to "screen" input "colour"
    When the patch is compiled
    Then the centre pixel is byte 0
    And the rendered image is entirely black
