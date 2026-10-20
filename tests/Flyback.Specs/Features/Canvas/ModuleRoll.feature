Feature: A module's knobs can be rolled
  Somebody exploring a module sends its knobs somewhere new at once, and steps back
  through the rolls with undo. A wired input, or one on a panel knob, stays put.

  Specified by ADR-0209.

  Background:
    Given a sine with its phase wired from a clock
    And the sine's "freq" on a panel knob
    And the patch is open in the editor
    And the sine is selected

  Scenario: Rolling moves the sine's own knobs and leaves the wired one and the panel's
    When the selection's knobs are rolled
    Then the sine's "amp" has moved from 1
    And the sine's "bias" has moved from 0
    And the sine's "phase" still rests at 0
    And the sine's "freq" still rests at 1

  Scenario: Undo takes a roll back, and redo puts it back again
    When the selection's knobs are rolled
    And the sine's "amp" is noted
    And that is undone
    Then the sine's "amp" still rests at 1
    When it is redone
    Then the sine's "amp" is what was noted
