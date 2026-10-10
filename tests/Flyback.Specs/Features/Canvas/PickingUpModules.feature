Feature: A module is held up while it is picked
  Pressing a module lifts it a little off the canvas, with a soft shadow under
  it, so what is being carried reads as separate from what is lying still.

  Scenario: A picked module casts a shadow
    Given a level of 0.3 shown through a module that halves it
    And the patch is open in the editor
    When the halving module is picked up
    Then a shadow falls under the halving module
