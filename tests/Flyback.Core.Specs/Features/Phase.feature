Feature: An oscillator accumulates its phase
  An oscillator adds up how far it has turned rather than multiplying its domain
  by its frequency. A new frequency bends the wave from where it stands instead of
  jumping it to wherever the product lands, and a jump is a click.

  Specified by ADR-0030, and by ADR-0048 for the clock it turns against.

  Scenario: Time is counted in seconds
    Given a patch containing:
      | name   | module   |
      | tone   | osc.sine |
      | screen | output   |
    And "tone" input "freq" is set to 1
    And "tone" output "out" is wired to "screen" input "left"
    And "screen" input "volume" is set to 1
    When the patch is compiled for audio
    Then the sound at 0.25 seconds is about 1
    And the sound at 0.5 seconds is about 0
    And the sound at 0.75 seconds is about -1

  # The frequency steps from 4 to 12 at 0.15 s. Multiplied, the phase would leap
  # from 0.6 of a turn to 1.8 and the wave by about a third of full scale; the
  # most a 12 Hz sine moves in a millisecond is 2π × 12 / 1000, under 0.08.
  Scenario: A frequency that jumps bends the wave rather than breaking it
    Given a patch containing:
      | name   | module     |
      | clock  | time       |
      | switch | math.step  |
      | pitch  | math.remap |
      | tone   | osc.sine   |
      | screen | output     |
    And "switch" input "edge" is set to 0.15
    And "clock" output "t" is wired to "switch" input "in"
    And "switch" output "out" is wired to "pitch" input "in"
    And "pitch" input "in low" is set to 0
    And "pitch" input "in high" is set to 1
    And "pitch" input "out low" is set to 4
    And "pitch" input "out high" is set to 12
    And "pitch" output "out" is wired to "tone" input "freq"
    And "tone" output "out" is wired to "screen" input "left"
    And "screen" input "volume" is set to 1
    When the patch is compiled for audio
    And the sound plays for 300 samples
    Then no two neighboring samples of the sound differ by more than 0.08
