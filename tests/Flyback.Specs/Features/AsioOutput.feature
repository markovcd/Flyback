Feature: Sound plays through an ASIO driver when it is picked
  On Windows, somebody with an audio interface picks ASIO on the Sound tab, and Flyback
  writes straight into the interface's driver, on its first two outputs, at Flyback's own
  sample rate.

  Specified by ADR-0192.

  Scenario: The driver takes the sound block by block on its first two outputs
    Given an ASIO driver is installed
    When the sound plays through it
    Then each block the driver plays carries the left and the right on its first two outputs

  Scenario: A driver running at another rate is set to Flyback's
    Given an ASIO driver running at 44100 Hz
    When the sound plays through it
    Then the driver runs at 48000 Hz

  Scenario: A driver held at another rate does not play at the wrong pitch
    Given an ASIO driver held at 44100 Hz by an outside clock
    When the sound is started through it
    Then it refuses, saying it cannot play at 48000 Hz
