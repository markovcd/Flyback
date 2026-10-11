Feature: A Dice throws the same for a number every time it comes round
  A Dice is a random with no memory. It reads a whole number, a step or a note
  or a cell, and throws a value and a face of a die for it, the same every time
  that number comes back, so a pattern picked at random repeats as a pattern.

  Scenario: A step that comes round throws what it threw before
    Given a die of 6 sides thrown for each of 16 steps a second, coming round every 2 seconds
    Then every round of 2 seconds throws what the first threw
    And every face from 0 to 5 comes up

  Scenario: Another seed throws another sequence
    Given two dice of different seeds thrown for each of 16 steps a second
    Then they throw different sequences
