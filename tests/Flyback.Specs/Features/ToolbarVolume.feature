Feature: The toolbar carries the Output's Volume
  Volume is the Output's knob, and the toolbar has it too, beside the seek bar, where
  it is found with nothing selected. Turning it there is the same edit as turning it
  on the Output's panel, and all the way down still closes the speakers.

  Scenario: Volume turned on the toolbar is the Output's, and is taken back like any edit
    Given a 220 Hz sine is playing
    And the patch is open in the editor
    When the toolbar's Volume is clicked all the way down
    Then the Output's Volume is 0
    When that is undone
    Then the Output's Volume is 1

  Scenario: A Volume a wire drives is not the toolbar's to turn
    Given a 220 Hz sine is playing
    And a level of 0.3 is wired into the Output's Volume
    And the patch is open in the editor
    Then the toolbar's Volume cannot be turned
