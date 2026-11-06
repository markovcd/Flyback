Feature: A renamed module still says what it is
  A name given to a module says what it does in this patch; its type says what it
  is. Both show, on the canvas and in the panel, so finding out that "across" is a
  Sine never means taking its name off.

  Scenario: A module with its own name shows only its family
    Given a sine driven by Time
    Then the sine's panel says it is "OSCILLATORS"

  Scenario: A renamed module names its type beside its family
    Given a sine driven by Time
    When the sine is renamed "across"
    Then the sine's panel says it is "SINE · OSCILLATORS"

  Scenario: Clearing the name leaves the family alone again
    Given a sine driven by Time
    When the sine is renamed "across"
    And the sine is renamed ""
    Then the sine's panel says it is "OSCILLATORS"
