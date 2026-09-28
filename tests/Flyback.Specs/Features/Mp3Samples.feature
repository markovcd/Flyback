Feature: A Sample plays an MP3
  A Sample plays an MP3 as it plays a WAV. The MP3 is read by ffmpeg: the one
  picked in the settings, or the one on PATH.

  Scenario: A Sample plays an MP3
    Given a Sample playing an MP3 made from half a second of a tone
    Then the speakers are not silent

  Scenario: An MP3 lasts exactly as long as the sound it was made from
    Given the length of an MP3 made from half a second of a tone
    Then the speakers play 0.5
