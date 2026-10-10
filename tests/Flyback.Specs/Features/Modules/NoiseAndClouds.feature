Feature: Noise hisses and Clouds drifts
  Two modules make randomness, and each is named for what it gives. Noise is the
  one to reach for a hiss, a hat or a flicker; Clouds is a smooth field for a
  sky, a terrain or a melody that wanders.

  Scenario: The module called Noise hisses
    Given the module called "Noise" is playing
    Then the sound is hiss

  Scenario: The module called Clouds is smooth across the screen
    Given the module called "Clouds" is on the screen
    Then the picture changes gradually from one pixel to the next
