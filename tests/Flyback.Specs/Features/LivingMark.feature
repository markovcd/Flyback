Feature: The mark in the About window comes alive
  Clicked seven times, the mark stops being a drawing and plays the patch it is
  a picture of, and that patch's text goes on the clipboard to be pasted into a
  patch of your own.

  Scenario: The living mark hands over its code
    Given the About window is open
    When its mark is clicked seven times
    Then the clipboard holds text that builds the picture the mark plays
