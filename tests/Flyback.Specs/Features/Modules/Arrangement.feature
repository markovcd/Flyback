Feature: An Arrangement says which parts play in each section
  An Arrangement steps through the sections of a piece and gives each of its
  parts a level for the section playing, so a piece can bring its parts in and
  out without a sequencer for each of them.

  Scenario: A part plays at its level in each section and comes round after the last
    Given an arrangement of sections 2 seconds long whose first part is "1 0 0.5 0"
    Then the sound is about 1 at 1 seconds
    And the sound is about 0 at 3 seconds
    And the sound is about 0.5 at 5 seconds
    And the sound is about 1 at 9 seconds

  Scenario: A level that glides gets there across its whole section
    Given an arrangement of sections 2 seconds long whose first part is "0 >1"
    Then the sound is about 0.5 at 3 seconds
    And the sound is about 0.75 at 3.5 seconds

  Scenario: It says which section is playing
    Given an arrangement of sections 2 seconds long whose first part is "1 1 1", telling which section is playing
    Then the sound is about 1 at 1 seconds
    And the sound is about 3 at 5 seconds
