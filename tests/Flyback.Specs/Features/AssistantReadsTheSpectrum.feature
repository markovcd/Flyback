Feature: The assistant can read a sound's spectrum
  When the assistant listens to a patch it is told which pitches are in the
  sound and how bright it is, as numbers computed from the samples.

  Scenario: The assistant listens to a steady tone
    Given the assistant has wired a 440 Hz tone to the speakers
    When the assistant listens to it
    Then it is told the sound has a tone at 440 Hz
