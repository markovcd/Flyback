Feature: The sound's oversampling is a render setting
  The sound is worked out at a multiple of the output rate before it is filtered down:
  2× unless Settings → Sound says 1× or 4×. A render and the viewer take it from the
  editor's settings as they take the rest, and --oversample overrides it for one run.

  Scenario Outline: A render oversamples as the settings say, unless told otherwise
    Given the text saved as "saw.fbks":
      """
      saw(freq: 3321.7) * 0.5 |> out.left
      out.volume = 1
      """
    And the editor's settings oversample the sound <set>
    When flyback-cli renders "saw.fbks" as "saw.wav" for 0.25 seconds<flags>
    Then "saw.wav" is the patch's sound worked out at <factor> times the output rate

    Examples:
      | set            | flags             | factor |
      | as they please |                   | 2      |
      | 1 times        |                   | 1      |
      | 1 times        | , --oversample 4  | 4      |
