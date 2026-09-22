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

  Scenario: An Auto remap fed by something with no range asks for its numbers
    Given a level of 0.5 remapped onto red
    Then Flyback points out that the Auto remap's input range has to be typed in
    And the screen shows 0.5, 0, 0
