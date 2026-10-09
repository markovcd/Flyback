Feature: Where more than one way of playing sound can play, the person picks
  A machine with WASAPI and an ASIO driver, or ALSA and a running JACK server, has two
  ways to its speakers. The one that ranks first plays until another is picked, and a
  pick that cannot play today gives way rather than silencing the sound.

  Specified by ADR-0191.

  Scenario: The one that ranks first plays until another is picked
    Given WASAPI and ASIO can both play
    Then the sound plays through WASAPI

  Scenario: A picked way of playing is the one that plays
    Given WASAPI and ASIO can both play
    When ASIO is picked to play through
    Then the sound plays through ASIO

  Scenario: A pick that cannot play today gives way
    Given WASAPI can play and ASIO cannot
    When ASIO is picked to play through
    Then the sound plays through WASAPI
