Feature: The knob panel can be randomized
  A performer reaches for new sounds and pictures by sending the panel's knobs
  somewhere new at once, keeps the knobs that must not move out of it, and can go
  back to where they were.

  Scenario: Randomizing moves every knob but the held ones, and back puts them where they were
    Given the text:
      """
      panel tone = 0.25
      panel level = 0.25, held
      saw(freq: 110) |> filter(cutoff: tone(200..4000)) |> out.left
      out.volume = level
      """
    When the knob panel is randomized
    Then the knob "tone" has moved from 0.25
    And the knob "level" still rests at 0.25
    When the knobs are taken back
    Then the knob "tone" still rests at 0.25
