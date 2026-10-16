Feature: The knob panel has switches and buttons
  A performer flips a sound or a picture between two settings with a switch, and
  sounds something only while a button is held, on screen or from a controller's
  button or pad. Randomizing flips switches and leaves buttons alone.

  Scenario: A click flips a switch, and it stays where it was left
    Given the text:
      """
      panel drop = 0, switch
      saw(freq: 110) |> filter(cutoff: drop(200..4000)) |> out.left
      """
    When the switch "drop" is clicked
    Then "drop" is on
    When the switch "drop" is clicked
    Then "drop" is off

  Scenario: A button is on only while it is held
    Given the text:
      """
      panel hit = 0, button
      sine(freq: 220, amp: hit) |> out.left
      """
    When the button "hit" is held down
    Then "hit" is on
    When the button "hit" is let go
    Then "hit" is off

  Scenario: Randomizing flips a switch and leaves a button alone
    Given the text:
      """
      panel drop = 0, switch
      panel hit = 0, button
      saw(freq: 110, amp: hit) |> filter(cutoff: drop(200..4000)) |> out.left
      """
    When the knob panel is randomized
    Then "drop" is on
    And "hit" is off
