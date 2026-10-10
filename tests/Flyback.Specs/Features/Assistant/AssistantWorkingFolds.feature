Feature: The assistant's working folds away from what it says
  What the assistant did, looked at and cost sits behind one line between the
  things it said, so a conversation can be read as the words alone.

  Scenario: What it did between two things it said is one line with a count
    Given the assistant said "Wiring it in.", did three things, and said "Done."
    Then the transcript has one folded line counting 3 steps
    And both sayings are outside it

  Scenario: What it is doing now stays open until it speaks again
    Given the assistant said "Wiring it in." and did three things
    Then the transcript has one open line counting 3 steps

  Scenario: The line opens what it holds
    Given the assistant said "Wiring it in.", did three things, and said "Done."
    When the folded line is pressed
    Then the transcript has one open line counting 3 steps
