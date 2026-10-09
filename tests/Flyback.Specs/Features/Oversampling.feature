Feature: The sound's oversampling is a render setting
  The sound is worked out at a multiple of the output rate before it is filtered down:
  2× unless Settings → Sound says none or 4×. A render and the viewer take it from the
  editor's settings as they take the rest, and --oversample overrides it for one run.

  Scenario Outline: A render oversamples as the settings say, unless told otherwise
    Given a saw bright enough to fold back, saved as "saw.fbks"
    And the editor's settings oversample the sound <set>
    When flyback-cli renders "saw.fbks" as "saw.wav" for 0.25 seconds<flags>
    Then "saw.wav" is the patch's sound worked out at <factor> times the output rate

    Examples:
      | set            | flags             | factor |
      | as they please |                   | 2      |
      | 1 times        |                   | 1      |
      | 1 times        | , --oversample 4  | 4      |

  Scenario: A live sound that keeps falling behind is worked out a step lower, and never back up
    Given live sound worked out at 4 times the output rate
    When a third of its buffers come late for 2 seconds
    Then it is worked out at 2 times the output rate
    When a third of its buffers come late for 3 seconds more
    Then it is worked out at 1 times the output rate
    When every buffer is on time for 10 seconds
    Then it is worked out at 1 times the output rate
    And it is not said to be behind

  Scenario: A live sound with a few late buffers is left where it is
    Given live sound worked out at 2 times the output rate
    When 3 of its buffers come late in 2 seconds
    Then it is worked out at 2 times the output rate
    And it is not said to be behind

  Scenario: A live sound that keeps falling behind with no oversampling is said to be behind, which the web viewer gives up
    Given live sound worked out at 1 times the output rate
    When a third of its buffers come late for 2 seconds
    Then it is worked out at 1 times the output rate
    And it is said to be behind, with no lower rate to go to

  Scenario: A live sound made anew is not judged until it has settled
    Given live sound worked out at 2 times the output rate
    When it is made anew
    And a third of its buffers come late for 3 seconds
    And every buffer is on time for 2 seconds
    Then it is worked out at 2 times the output rate
