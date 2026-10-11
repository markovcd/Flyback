Feature: A Window is on between two points and off outside them
  A Window reads a value and is 1 while it is between its two points, 0
  outside. Each end is hard, or ramps inside the window over as much as its
  rise or its fall says, so a part can be let in for a stretch of time, a band
  of the picture or the last bar of a section.

  Scenario: A hard window is on between its points and off either side
    Given a window from 2 to 5 seconds
    Then the sound is about 0 at 1.9 seconds
    And the sound is about 1 at 2 seconds
    And the sound is about 1 at 4.9 seconds
    And the sound is about 0 at 5 seconds

  Scenario: A soft window ramps up and down inside its points, each at its own pace
    Given a window from 2 to 5 seconds that takes 0.5 seconds to turn on and 1 to turn off
    Then the sound is about 0 at 2 seconds
    And the sound is about 0.5 at 2.25 seconds
    And the sound is about 1 at 3 seconds
    And the sound is about 0.5 at 4.5 seconds
    And the sound is about 0 at 5 seconds
