Feature: The editor says how fast a patch's sound renders
  Somebody building a heavy sound wants to know whether it keeps up before it stutters:
  the status bar says how many times real time the sound renders at and its oversampling,
  and says neither for a patch with no sound. It leaves counting modules and wires to the canvas.

  Scenario: The status bar says how fast the sound renders
    Given a 220 Hz sine is playing
    And the speakers play whatever they are handed
    And the patch is open in the editor
    When the speakers have played long enough to time the sound
    Then the status bar says how many times real time the sound renders at
    And the status bar counts no modules or wires

  Scenario: The status bar says nothing of a sound a patch does not have
    Given a rainbow across the screen
    And the speakers play whatever they are handed
    And the patch is open in the editor
    When the speakers have played for 1 second
    Then the status bar says nothing of how fast the sound renders
    And the status bar says nothing of oversampling
