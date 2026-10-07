Feature: A melody nobody wrote
  The Effects plugin's Wandering tune plays a melody that is never written down: a
  wandering value snapped to a pentatonic, played when a coin lets a note through,
  and drawn as a score that scrolls as it plays.

  Scenario: The Wandering tune opens and plays with nothing wrong
    Given the shipped preset "Wandering tune"
    Then it opens with nothing wrong
