Feature: A module can be switched off without unpatching it
  Switching a module off takes it out of the signal path. What was patched into
  it arrives where it would have gone, as if the module were a plain wire.

  Specified by ADR-0117.

  Scenario: A module that is on does its work
    Given a level of 0.3 shown through a module that halves it
    Then the screen shows 0.15

  Scenario: A module that is off passes its input straight on
    Given a level of 0.3 shown through a module that halves it
    When the halving module is switched off
    Then the screen shows 0.3

  Scenario: Several modules switched off in a row pass the signal through them all
    Given a level of 0.3 shown through two modules that each halve it
    When both halving modules are switched off
    Then the screen shows 0.3

  Scenario: With nothing patched into it, a switched-off module is as good as unplugged
    Given a switched-off module with nothing patched in, feeding an adder set to 0.1 plus 0.1
    Then the screen shows 0.2

  Scenario: A module that is off costs nothing to run
    Given the horizontal position shown through a module that halves it
    When the halving module is switched off
    Then the halving costs nothing
