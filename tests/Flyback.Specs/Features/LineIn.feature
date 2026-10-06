Feature: A Line In plays what the microphone hears
  A Line In patched into the speakers plays whatever the sound input hears, as it
  plays. A render has no microphone, so it hears the sound file it is given, and
  silence where it is given none.

  Specified by ADR-0178.

  Scenario: A Line In plays the sound it hears
    Given a Line In is patched into the speakers
    When it hears a 440 Hz tone
    Then the speakers play a 440 Hz tone

  Scenario: A Line In with nothing to hear is silent, not broken
    Given a Line In is patched into the speakers
    Then the patch is accepted without complaint
    And the speakers are silent

  Scenario: A render of a Line In hears the sound file it is given
    Given a 440 Hz tone saved as "voice.wav"
    And the text saved as "listen.fbks":
      """
      audio.in() |> out.left
      out.volume = 1
      """
    When flyback-cli renders "listen.fbks" as "heard.wav" for 1 seconds, --input voice.wav
    Then "heard.wav" plays a 440 Hz tone

  Scenario: A Line In in the web viewer plays what the page's microphone hears
    Given a Line In is patched into the speakers
    And the web viewer's microphone hears a 440 Hz tone
    When the patch plays in the web viewer for 1 second
    Then the web viewer says it reads a Line In
    And the web viewer plays a 440 Hz tone

  Scenario: A patch without a Line In does not ask the web viewer for the microphone
    Given the shipped preset "Sidebands"
    When it plays in the web viewer for 0.1 seconds
    Then the web viewer says it reads no Line In

  Scenario: A render of a Line In without a sound file is silent
    Given the text saved as "listen.fbks":
      """
      audio.in() |> out.left
      out.volume = 1
      """
    When flyback-cli renders "listen.fbks" as "heard.wav" for 1 seconds
    Then "heard.wav" is silent
