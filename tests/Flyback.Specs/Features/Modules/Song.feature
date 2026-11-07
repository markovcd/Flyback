Feature: The Song plays each of its named parts at its own length
  A patch has at most one Song. Each of its parts is named, and loops over as
  many sections as it has, so parts of different lengths come round at
  different times and the piece drifts the way a long arrangement would.

  Scenario: A part comes round after its own last section
    Given a song of sections 2 seconds long whose parts are "kick: 1 0 bass: 1 0 0.5", playing its bass
    Then the sound is about 1 at 1 seconds
    And the sound is about 0 at 3 seconds
    And the sound is about 0.5 at 5 seconds
    And the sound is about 1 at 7 seconds

  Scenario: A shorter part comes round sooner
    Given a song of sections 2 seconds long whose parts are "kick: 1 0 bass: 1 0 0.5", playing its kick
    Then the sound is about 1 at 1 seconds
    And the sound is about 0 at 3 seconds
    And the sound is about 1 at 5 seconds
