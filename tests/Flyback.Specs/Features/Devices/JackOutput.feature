Feature: Sound plays through a JACK server when one is running
  Where somebody started a JACK server, Flyback plays through it rather than through
  ALSA, at the server's sample rate, and the server clocks the sound.

  Specified by ADR-0190.

  Scenario: A running JACK server is where the sound goes
    Given a JACK server is running
    Then the sound plays through JACK

  Scenario: The server's clock drives the sound
    Given a JACK server is running
    When the sound plays through JACK for 1 seconds
    Then the server has taken 1 seconds of sound at its own rate
