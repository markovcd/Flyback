Feature: A render starts at the second it is asked to
  flyback-cli render plays the patch up to --from without recording it, so
  taking seconds 2 to 3 of a patch is a render of those seconds and not of the
  first three.

  Scenario: A sound from a second in is of that second
    Given the text saved as "late.fbks":
      """
      let gate = step(edge: 2, in: time().t)
      sine(freq: 440) * gate |> out.left
      out.volume = 1
      """
    When flyback-cli renders "late.fbks" as "early.wav" for 1 seconds
    And flyback-cli renders "late.fbks" as "late.wav" for 1 seconds, --from 2
    Then "early.wav" is silent
    And "late.wav" plays a 440 Hz tone
