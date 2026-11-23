Feature: A patch keeps time with the kicks it hears
  A Beat Follow hears a tempo in a gate of hits, such as the kick a microphone
  hears, and keeps a beat to it. In a break it carries on at the last tempo.

  Scenario: A steady kick gives its tempo
    Given a kick every 0.5 seconds, followed for its bpm
    When it plays for 8 seconds
    Then that sample is about 120

  Scenario: A kick slower than the tempos it may settle on is heard at twice its pace
    Given a kick every 1 seconds, followed for its bpm
    When it plays for 8 seconds
    Then that sample is about 120

  Scenario: The beat carries on through a break
    Given a kick every 0.5 seconds that stops after 4 seconds, followed for its beat
    When it plays for 10 seconds
    Then it fires every 0.5 seconds from 4 seconds on
