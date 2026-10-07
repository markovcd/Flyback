Feature: A module is found by what a phrase means
  Where what is typed spells no module's name, the module list, and
  flyback-cli modules --find, ask the decision model which modules it
  describes, and list them likeliest first.

  Scenario: A phrase finds the module it describes
    Given a decision model that takes "a mirror maze of shards" to mean a Kaleidoscope
    When flyback-cli finds the modules "a mirror maze of shards" describes
    Then the first module found is the Kaleidoscope

  Scenario: Without a decision model nothing is found by meaning
    Given decisions are turned off
    When flyback-cli finds the modules "a mirror maze of shards" describes
    Then the command fails, saying no decision model is in use
