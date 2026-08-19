Feature: One patch, one Output, two programs
  The same modules drive the screen and the speakers, through one block that
  carries both. Compilation walks back from the socket a given program is rooted
  at, so a patch still yields one program per sink — and each still pays only for
  what actually reaches it.

  Specified by ADR-0022, amended by ADR-0037.

  Scenario: The two halves compile to independent programs
    Given a patch containing:
      | name   | module        |
      | coords | coord         |
      | grain  | pattern.noise |
      | clock  | time          |
      | tone   | osc.sine      |
      | out    | output        |
    And "coords" output "x" is wired to "grain" input "x"
    And "grain" output "out" is wired to "out" input "color"
    And "clock" output "t" is wired to "tone" input "in"
    And "tone" output "out" is wired to "out" input "left"
    When the patch is compiled for video
    Then compilation reports no issues
    And the program contains at least one "Noise3" op
    And the program contains no "Sin" ops
    When the patch is compiled for audio
    Then compilation reports no issues
    And the program contains at least one "Sin" op
    And the program contains no "Noise3" ops

  # ADR-0009 named the normalled jack; here it is literally one.
  Scenario: An unpatched right channel carries the left one
    Given a patch containing:
      | name  | module   |
      | clock | time     |
      | tone  | osc.sine |
      | out   | output   |
    And "tone" input "freq" is set to 220
    And "clock" output "t" is wired to "tone" input "in"
    And "tone" output "out" is wired to "out" input "left"
    When the patch is compiled for audio
    Then both audio channels are identical
    And the audio is not silent

  Scenario: A patch with nothing wired to the speakers is silent, not broken
    Given a patch containing:
      | name | module     |
      | tint | color.hsv |
      | out  | output     |
    And "tint" output "color" is wired to "out" input "color"
    When the patch is compiled for audio
    Then compilation reports no issues
    And the audio is silent

  # The mirror of the scenario above. Neither half is nagged about the other:
  # a patch built for the speakers is as deliberate as one built for the screen,
  # and saying so on every edit is noise rather than help.
  Scenario: A patch built for the speakers alone is not nagged about the screen
    Given a patch containing:
      | name  | module   |
      | clock | time     |
      | tone  | osc.sine |
      | out   | output   |
    And "clock" output "t" is wired to "tone" input "in"
    And "tone" output "out" is wired to "out" input "left"
    When the patch is compiled for video
    Then compilation reports no issues
    And the rendered image is entirely black

  # An Output nothing reaches produces one flat color and silence, which looks
  # exactly like a patch that is working — so the one thing that can be said
  # about it is said, once, whichever program is being built.
  Scenario: An Output with nothing wired into it at all is remarked on
    Given a patch containing:
      | name | module |
      | out  | output |
    When the patch is compiled for audio
    Then compilation reports an issue containing "Nothing is wired into the Output"
    And compilation reports nothing wrong
    And the audio is silent
