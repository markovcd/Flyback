Feature: The patch is played from a row of its own along the foot of the window
  Pause, rewind, the seek bar, the length, loop, Volume and record stand in one row
  along the foot of the window, every button in it big enough for a finger, and the
  seek bar as wide as the window leaves it. The toolbar keeps one row in a window
  730 pixels wide. The row can be put away for its height, and a line still shows
  where the patch is.

  Background:
    Given a rainbow across the screen

  Scenario: The toolbar keeps one row in a window 730 pixels wide
    Given the screen is 730 pixels wide
    And the patch is open in the editor
    Then the toolbar is one row tall
    And every transport button is on the screen and big enough for a finger

  Scenario: A finger slid along the seek bar takes the patch's clock with it
    Given the patch is open in the editor
    When a finger slides along the seek bar from 10 to 40 seconds
    Then the patch's clock is at about 40 seconds

  Scenario: Putting the transport row away gives its height to the canvas and leaves a line of the playhead
    Given the patch is open in the editor
    When the transport button is let out
    Then the transport row is away, and the playhead line shows
    And the canvas has grown by the row's height
    When the transport button is pressed in
    Then the transport row is back, and the playhead line is gone

  Scenario: On a phone held upright the length, loop and Volume wait behind one button
    Given the screen is a phone held upright
    And the patch is open in the editor
    Then every transport button is on the screen and big enough for a finger
    And the seek bar is at least 120 pixels wide
    And the length is not on the screen
    When the transport row's more button is pressed
    Then the length is on the screen

  Scenario: A page's transport row plays and rewinds and records nothing
    Given the editor is in a page
    And the patch is open in the editor
    Then the transport row has "pause, rewind" and none of "record"
