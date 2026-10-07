Feature: A decision model can be asked from the command line
  flyback-cli decide asks the decision model the editor is set to typed
  questions about a piece of text, and answers each with a probability, so a
  script can act on how sure the answer is.

  Scenario: A yes-no question is answered in the System One format
    Given the decision models that are installed
    When flyback-cli decides whether "we were billed twice" is about money, as JSON
    Then the answer is a yes-no with a probability between 0 and 1

  Scenario: Decisions turned off ask nothing
    Given the decision models that are installed
    And decisions are turned off
    When flyback-cli decides whether "we were billed twice" is about money, as JSON
    Then the command fails, saying no decision model is in use
