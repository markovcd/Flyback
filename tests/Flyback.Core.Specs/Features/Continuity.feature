Feature: A playing patch keeps its memory across an edit
  Every edit recompiles the whole patch, but a module the edit did not replace
  keeps what it remembers: an oscillator its phase, a loop its last value. Turning
  a knob while the sound plays changes the sound, not where it had got to.

  Specified by ADR-0067, with ADR-0021 for the recompile.

  # 125 ms at 10 Hz leaves the wave at its crest. Started again from nothing, it
  # would drop from about 1 to 0 in one sample; carried on, no step is larger than
  # a 12 Hz sine moves in a millisecond.
  Scenario: A tone keeps its phase when its frequency is turned
    Given a patch containing:
      | name   | module   |
      | tone   | osc.sine |
      | screen | output   |
    And "tone" input "freq" is set to 10
    And "tone" output "out" is wired to "screen" input "left"
    And "screen" input "volume" is set to 1
    When the patch is compiled for audio
    And the sound plays for 125 samples
    And "tone" input "freq" is turned to 12
    And the sound plays for 100 samples
    Then no two neighboring samples of the sound differ by more than 0.08

  # The loop has settled on a half. Kept, the next sample is half of it plus the
  # new 0.3; forgotten, it would be 0.3 alone.
  Scenario: A loop keeps its last value when a knob in it is turned
    Given a patch containing:
      | name   | module   |
      | half   | math.mul |
      | nudge  | math.add |
      | screen | output   |
    And "half" input "b" is set to 0.5
    And "nudge" input "b" is set to 0.25
    And "half" output "out" is wired to "nudge" input "a"
    And "nudge" output "out" is wired to "half" input "a"
    And "nudge" output "out" is wired to "screen" input "left"
    And "screen" input "volume" is set to 1
    When the patch is compiled for audio
    And the sound plays for 30 samples
    And "nudge" input "b" is turned to 0.3
    And the sound plays for 1 sample
    Then the last sample of the sound is about 0.55
