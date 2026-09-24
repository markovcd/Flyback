Feature: A patch can be written as text
  The text language is a second way to write the same patch: anything built on
  the canvas can be written out, and anything written reads back as the patch it
  describes.

  Specified by ADR-0065.

  Scenario: A patch written as text plays
    Given the text:
      """
      sine(freq: 220) |> out.left
      """
    Then it reads without complaint
    And the speakers are not silent

  Scenario: A patch written out as text reads back as the same patch
    Given a rainbow across the screen
    When the patch is written out as text and read back
    Then the picture is as it was

  Scenario: Arithmetic on a chain is written out as arithmetic
    Given the text:
      """
      let bent = sine(freq: 3) |> clamp()
      bent * x |> out.color
      """
    When the patch is written out as text and read back
    Then the text has the line "bent * x |> out.color"
    And the picture is as it was

  Scenario: A shipped preset can be read as text by its name
    When the preset "Plasma" is printed from the command line
    Then it reads without complaint
    And the screen is not black

  Scenario: Arithmetic calling a function is written out as arithmetic
    Given the text:
      """
      floor(x * 8) / 8 |> out.color
      """
    When the patch is written out as text and read back
    Then the text has the line "floor(x * 8) / 8 |> out.color"
    And the picture is as it was

  Scenario: A panel knob scales a sum it stands in
    Given the text:
      """
      panel level = 0
      sine(freq: 220) * level(0..1) |> out.left
      """
    Then it reads without complaint
    And the speakers are silent
    When the patch is written out as text and read back
    Then the text has the line "sine(freq: 220) * level(0..1) |> out.left"

  Scenario: Arithmetic nobody named is written out named after what it drives
    Given the text:
      """
      let pitch = x * 200 + 300
      sine(freq: pitch) + saw(freq: pitch) |> out.left
      """
    And its modules were never named
    When the patch is written out as text and read back
    Then the text has the line "let freq = x * 200 + 300"

  Scenario: A mistake is pointed out on its own line
    Given the text:
      """
      rings() |> out.color
      kaleidoscop() |> out.color
      """
    Then the complaint quotes line 2

  Scenario: The placeholder puts the signal where it says
    Given the text:
      """
      let held = value(1)
      sine(freq: 220) * (held |> adsr(gate: _, attack: 1ms)) |> out.left
      """
    Then it reads without complaint
    And the speakers are not silent

  # A played patch keeps its knobs through the text.
  Scenario: A panel knob and the sockets that follow it are written in the text
    Given the text:
      """
      panel level = 0.8, label: "Level", cc: 7, device: "midi:test"
      sine(freq: 220, amp: level) |> out.left
      """
    When the patch is written out as text and read back
    Then the panel has a knob "Level" resting at 0.8
    And the speakers are not silent

  Scenario: A panel knob left somewhere by hand rests there in the text
    Given the text:
      """
      panel level = 0.8
      sine(freq: 220, amp: level) |> out.left
      """
    When the panel knob "level" is left at 0.25
    Then the text has the line "panel level = 0.25"
    And the panel has a knob "level" resting at 0.25

  # A box drawn on the canvas is part of the patch, and the text keeps it.
  Scenario: A patch's groups are written in its text and read back shut
    Given the text:
      """
      group "Voice" {
        let tone = sine(freq: 220)
        let shaped = tone |> drive(drive: 2)
      }
      shaped |> out.left
      """
    When the patch is written out as text and read back
    Then the patch has a shut group "Voice" of 2 modules
    And the speakers are not silent
