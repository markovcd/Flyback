Feature: A module is found by what a phrase means
  Where what is typed spells no module's name, the module list, and
  flyback-cli modules --find, ask the decision model which modules it
  describes, and list them likeliest first.

  Scenario: A phrase finds the module it describes
    Given a decision model that takes "a mirror maze of shards" to mean a Kaleidoscope
    When flyback-cli finds the modules "a mirror maze of shards" describes
    Then the first module found is the Kaleidoscope

  Scenario: Finding modules asks the model as it was set up for finding modules
    Given a decision model that takes "a mirror maze of shards" to mean a Kaleidoscope only when its model is "typed"
    And flyback-cli sets its model to "typed" for finding modules alone
    When flyback-cli finds the modules "a mirror maze of shards" describes
    Then the first module found is the Kaleidoscope

  Scenario: A word a module is known by finds it with no model at all
    Given decisions are turned off
    When flyback-cli finds the modules "portamento" describes
    Then the first module found is the "Slew"

  Scenario: Without a decision model nothing is found by meaning
    Given decisions are turned off
    When flyback-cli finds the modules "a mirror maze of shards" describes
    Then the command fails, saying no decision model is in use
