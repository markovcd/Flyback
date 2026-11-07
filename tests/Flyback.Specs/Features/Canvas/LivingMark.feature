Feature: The mark in the About window comes alive
  Clicked seven times, the mark stops being a drawing and plays a close-up of the
  tube in a patch: an old set in a dark room drawing the logo, with a loop playing
  on it. The whole scene's text goes on the clipboard to be pasted into a patch of
  your own.

  Scenario: The living mark hands over its code
    Given the About window is open
    When its mark is clicked seven times
    Then the clipboard holds text that builds the whole scene the mark is a close-up of
