Feature: A patch sounds the same an hour in
  A performance or a long render runs for hours. The clock and the arithmetic
  keep enough precision that nothing drifts, wobbles or clicks late in a run.

  Specified by ADR-0032.

  Scenario: A tone an hour in plays exactly as it did at the start
    Given a 10 Hz sine is playing
    Then a second of it an hour in sounds as its first second did

  Scenario: Noise read off a fast clock is still noise a week in
    Given chip noise is playing
    Then a second of it a week in is noise as loud as its first second
