Feature: An export is the patch, exactly
  What flyback-cli render writes is what the patch makes, and the same every
  time it is asked. A patch that has noise, remembers what it made and filters
  what it hears is the one where that is hardest to keep.

  Background:
    Given a drifting rainbow that trails, with noise through a filter and a delay at the speakers

  Scenario Outline: Exporting the same patch twice writes the same file
    When it is exported as "<file>" twice
    Then the two files are the same to the byte

    Examples:
      | file      |
      | still.png |
      | sound.wav |
      | clip.avi  |

  Scenario: An export sounds exactly as the patch does in the editor
    When the editor plays it for 2 seconds
    And it is exported as "sound.wav" 2 seconds long
    Then the exported sound is what the editor played, sample for sample
