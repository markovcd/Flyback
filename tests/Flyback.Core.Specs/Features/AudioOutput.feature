Feature: One patch, two sinks
  The same modules drive the screen and the speakers. Compilation is rooted at a
  sink, so a patch yields one program per sink — and each pays only for what it
  actually reaches.

  Specified by ADR-0022.

  Scenario: The two sinks compile to independent programs
    Given a patch containing:
      | name    | module        |
      | coords  | coord         |
      | grain   | pattern.noise |
      | clock   | time          |
      | tone    | osc.sine      |
      | screen  | video.output  |
      | speaker | audio.output  |
    And "coords" output "x" is wired to "grain" input "x"
    And "grain" output "out" is wired to "screen" input "colour"
    And "clock" output "t" is wired to "tone" input "in"
    And "tone" output "out" is wired to "speaker" input "left"
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
      | name    | module       |
      | clock   | time         |
      | tone    | osc.sine     |
      | speaker | audio.output |
    And "tone" input "freq" is set to 220
    And "clock" output "t" is wired to "tone" input "in"
    And "tone" output "out" is wired to "speaker" input "left"
    When the patch is compiled for audio
    Then both audio channels are identical
    And the audio is not silent

  Scenario: A patch with no Audio Output is silent, not broken
    Given a patch containing:
      | name   | module       |
      | tint   | colour.hsv   |
      | screen | video.output |
    And "tint" output "colour" is wired to "screen" input "colour"
    When the patch is compiled for audio
    Then compilation reports no issues
    And the audio is silent

  # Silence is what an Audio Output holding its knobs produces, and it looks
  # exactly like a patch that is working — so the one thing that can be said
  # about it is said.
  Scenario: An Audio Output with nothing wired into it is remarked on
    Given a patch containing:
      | name    | module       |
      | speaker | audio.output |
    When the patch is compiled for audio
    Then compilation reports an issue containing "Nothing is wired into the Audio Output"
    And compilation reports nothing wrong
    And the audio is silent

  # The mirror of the scenario above. Neither sink is nagged about the other:
  # a patch built for the speakers is as deliberate as one built for the screen,
  # and saying so on every edit is noise rather than help.
  Scenario: A patch built for the speakers alone is not nagged about the screen
    Given a patch containing:
      | name    | module       |
      | speaker | audio.output |
    When the patch is compiled for video
    Then compilation reports no issues
