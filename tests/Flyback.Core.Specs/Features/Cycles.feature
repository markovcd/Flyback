Feature: A loop carries the evaluation before
  A patch may be wired back into itself. The wire that closes the loop carries
  what the loop computed one evaluation earlier: the previous sample at the
  speakers, the previous frame at this pixel on the screen.

  Specified by ADR-0075, resting on ADR-0074.

  Background:
    Given a patch containing:
      | name   | module   |
      | half   | math.mul |
      | nudge  | math.add |
      | screen | output   |
    And "half" input "b" is set to 0.5
    And "nudge" input "b" is set to 0.25
    And "half" output "out" is wired to "nudge" input "a"
    And "nudge" output "out" is wired to "half" input "a"

  Scenario: A cycle compiles rather than being reported
    Given "nudge" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then compilation reports no issues

  # Each sample is half the one before plus a quarter, so the loop settles on a
  # half. The first sample reads nothing back.
  Scenario: At the speakers a loop remembers the sample before
    Given "nudge" output "out" is wired to "screen" input "left"
    And "screen" input "volume" is set to 1
    When the patch is compiled for audio
    Then the sound begins "0.25, 0.375, 0.4375, 0.46875"

  Scenario: On the screen a loop remembers the frame before at this pixel
    Given "nudge" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then rendering 1 frame gives a centre brightness of about 0.25
    And rendering 2 frames gives a centre brightness of about 0.375
    And rendering 3 frames gives a centre brightness of about 0.4375

  Scenario: Rewinding forgets what a loop remembered
    Given "nudge" output "out" is wired to "screen" input "color"
    When the patch is compiled
    Then rewinding after 5 frames and rendering 1 frame gives a centre brightness of about 0.25
