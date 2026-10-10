Feature: A drum machine plays and pauses the patch
  A drum machine or sequencer the patch listens to drives the editor's transport:
  its Start plays the patch from the top, its Stop pauses it, and its Continue
  plays on from where it paused. The MIDI settings can turn this off.

  Background:
    Given the beats of a drum machine's clock on the speakers
    And the drum machine is plugged in

  Scenario: Start plays the patch from the top, and Stop pauses it
    Given the patch is open in the editor
    And the patch is paused
    When the seek bar is clicked at 10 seconds
    And the drum machine presses Start
    Then the patch plays from the top
    When the drum machine presses Stop
    Then the patch pauses

  Scenario: Continue plays on from where the patch paused
    Given the patch is open in the editor
    And the patch is paused
    When the seek bar is clicked at 10 seconds
    And the drum machine presses Continue
    Then the patch plays on from about 10 seconds

  Scenario: Told not to, the editor leaves its transport alone
    Given the settings say not to play and pause with an instrument
    And the patch is open in the editor
    And the patch is paused
    When the seek bar is clicked at 10 seconds
    And the drum machine presses Start
    Then the patch stays paused at about 10 seconds
