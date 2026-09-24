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

  # The text is refused whole, so what the mistake would have made is not complained about again.
  Scenario: A line that fails is the one complaint, however often what it names is read
    Given the text:
      """
      let tone = sinee(freq: 220)
      tone |> out.left
      tone * 0.5 |> out.right
      """
    Then the complaint quotes line 1
    And that is the only complaint

  # A second binding would leave every reader of the name guessing which it means.
  Scenario: A comment written the way other languages write one is pointed at the right way
    Given the text:
      """
      // --- CLOCK & TIMING ---
      let tempo = tempo(bpm: 100)
      """
    Then the complaint quotes line 1
    And that is the only complaint
    And the complaint says "a comment starts with '#'"
    And the complaint says "group"

  Scenario: A name is bound once
    Given the text:
      """
      let hum = sine(freq: 110)
      let hum = sine(freq: 220)
      hum |> out.left
      """
    Then the complaint quotes line 2
    And the complaint says "already bound on line 1"

  Scenario: The clock cannot be renamed into something else
    Given the text:
      """
      let t = 5
      x |> sine(freq: 2, phase: t) |> out.left
      """
    Then the complaint quotes line 1
    And the complaint says "the clock"

  Scenario: Wiring a socket twice is pointed out rather than quietly replaced
    Given the text:
      """
      rings(freq: 3) |> out.color
      rings(freq: 5) |> out.color
      """
    Then the complaint quotes line 2
    And the complaint says "already wired on line 1"

  # Where a pipe lands is written on the line, never guessed from the arguments beside it.
  Scenario: A pipe into a module with no 'in' is told where to land
    Given the text:
      """
      pulse(freq: 2) |> adsr(decay: 240ms) |> out.left
      """
    Then the complaint quotes line 1
    And the complaint says "adsr(gate: _)"

  Scenario: The placeholder puts the signal where it says
    Given the text:
      """
      let held = value(1)
      sine(freq: 220) * (held |> adsr(gate: _, attack: 1ms)) |> out.left
      """
    Then it reads without complaint
    And the speakers are not silent

  Scenario: A pipeline inside a call's brackets is pointed out
    Given the text:
      """
      let steps = notes(rate: 4) [ A3 C4 ]
      saw(freq: steps |> note(note: _)) |> out.left
      """
    Then the complaint quotes line 2
    And the complaint says "Bind it with 'let'"

  Scenario: Setting a knob twice is pointed out rather than the last one winning
    Given the text:
      """
      out.volume = 0.5
      out.volume = 0.6
      """
    Then the complaint quotes line 2
    And the complaint says "already set on line 1"

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

  Scenario: A patch needing a plugin this build lacks says so once, by name
    Given the text:
      """
      requires nobody.here
      blob() |> out.color
      blob() |> out.left
      """
    Then the complaint quotes line 1
    And the complaint says "no plugin 'nobody.here'"
    And that is the only complaint

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
