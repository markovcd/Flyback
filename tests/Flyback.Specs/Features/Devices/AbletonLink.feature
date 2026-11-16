Feature: A patch keeps time with the apps in an Ableton Link session
  A Clock In set to follow the Ableton Link session keeps time with the other
  apps on the same network, Ableton Live, Bitwig or a drum app on a phone, with
  no cable and no setup. Its beats run at the session's tempo and in phase with
  its bars, and hold while an app that uses start and stop has stopped it.
  Flyback joins the network only while a playing patch follows the session, on
  the desktop and on Android. flyback-cli ableton joins it for a while and says
  what it hears: the session, how many other apps are in it, its tempo and its
  beat.

  Scenario: A sequencer steps with the session's beats
    Given a sequencer of 0.2, 0.4, 0.6, 0.8 stepping once a beat of the Ableton Link session
    When the Ableton Link session plays 2 beats at 120 bpm
    Then the sound is about 0.2 at 0.25 seconds
    And the sound is about 0.4 at 0.75 seconds

  Scenario: The beats are in phase with the session's bars
    Given a sequencer of 0.2, 0.4, 0.6, 0.8 stepping once a beat of the Ableton Link session
    When the Ableton Link session plays 1 beat at 120 bpm, starting 2 beats into a bar
    Then the sound is about 0.6 at 0.25 seconds

  Scenario: The tempo another app sets is on the bpm output
    Given the bpm of the Ableton Link session's clock on the speakers
    When the Ableton Link session plays 2 beats at 140 bpm
    Then the sound is about 140 at 0.8 seconds

  Scenario: An app pressing Stop holds the beat
    Given the beats of the Ableton Link session's clock on the speakers
    When the Ableton Link session plays 1 beat at 120 bpm
    And an app in the Ableton Link session presses Stop
    And it plays on for 0.9 seconds
    Then the sound is about 1 at 1.4 seconds
