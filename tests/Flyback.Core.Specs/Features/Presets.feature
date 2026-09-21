Feature: Every shipped preset works
  The presets are the first thing anyone opens. Each one has to open cleanly,
  and survive being saved or written out as text without changing.

  Specified by ADR-0020 for files and ADR-0065 for text.

  Background:
    Given every shipped preset

  # A remark is allowed: the empty preset says there is nothing to see, and a
  # preset waiting for a sound file says so.
  Scenario: Every preset opens with nothing wrong
    Then each one opens with nothing wrong

  Scenario: Every preset saved to a file opens as the same instrument
    Then each one saved and opened again is the same instrument

  Scenario: Every preset written out as text reads back as the same instrument
    Then each one written out as text and read back is the same instrument
