Feature: One patch drives both the screen and the speakers
  The same modules make the picture and the sound, through one Output that takes
  both. Each side costs only what actually reaches it.

  Specified by ADR-0022, amended by ADR-0037.

  Scenario: The picture and the sound each pay only for themselves
    Given a cloud picture on the screen and a sine tone at the speakers
    Then the patch is accepted without complaint
    And drawing the picture does not compute the tone
    And playing the sound does not compute the picture

  # The right channel is normalled to the left, like a jack with nothing in it.
  Scenario: A tone patched into the left channel alone plays from both speakers
    Given a 220 Hz sine is playing
    Then both speakers play the same sound

  Scenario: A patch with nothing for the speakers is silent, not broken
    Given a rainbow across the screen
    Then the patch is accepted without complaint
    And the speakers are silent

  # Neither half is nagged about the other: a patch built for the speakers is as
  # deliberate as one built for the screen.
  Scenario: A patch with nothing for the screen is black, not broken
    Given a 220 Hz sine is playing
    Then the patch is accepted without complaint
    And the screen is black
