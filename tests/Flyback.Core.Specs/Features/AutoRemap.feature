Feature: An Auto remap takes its ranges from what it is wired between
  Its knobs are fractions of the range of whatever feeds it and of the socket it
  feeds, so a sweep is set without knowing either range. Where one end has no
  range, that pair of knobs is plain numbers, and Flyback says so.

  Scenario: A wave at its peak lands at the top of the slice picked from a color
    Given a sine at its peak remapped onto red, from 0.2 to 0.5 of red's range
    Then the screen shows 0.5, 0, 0

  Scenario: A wave at its trough lands at the bottom of the slice
    Given a sine at its trough remapped onto red, from 0.2 to 0.5 of red's range
    Then the screen shows 0.2, 0, 0

  Scenario: A wire between two different ranges is offered an Auto remap
    Given a sine wired into a filter's cutoff
    Then Flyback offers to fit the ranges on the wire

  Scenario: A wave swinging past what its socket takes is pointed out
    Given a sine wired into a color's brightness
    Then Flyback points out that the sine swings past what the brightness takes

  Scenario: A gate reads a threshold, so a wave into one is not pointed out
    Given a pulse wired into an envelope's gate
    Then the patch is accepted without complaint

  Scenario: A wire between matching ranges is left alone
    Given a sine wired into a filter's input
    Then Flyback offers nothing on the wire

  Scenario: An Auto remap fed by something with no range asks for its numbers
    Given a level of 0.5 remapped onto red
    Then Flyback points out that the Auto remap's input range has to be typed in
    And the screen shows 0.5, 0, 0
