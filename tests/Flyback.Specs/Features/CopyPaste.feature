Feature: Copied modules paste with their wiring and knobs
  What is copied is a small patch. Pasting it adds new modules wired among
  themselves as the originals were, and leaves the originals alone.

  Specified by ADR-0045.

  Scenario: A pasted pair keeps the wire between them and their knobs
    Given a level of 0.3 shown through a module that halves it
    When the level and the halving module are copied and pasted
    Then there are two levels and two halving modules
    And the pasted halving module is fed by the pasted level
    And the pasted halving module still halves
    And the screen shows 0.15

  Scenario: Pasting a whole patch never adds a second Output
    Given a level of 0.3 on the screen
    When everything is copied and pasted
    Then the patch still has one Output
