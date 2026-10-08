Feature: A use of the decision model is set up on its own
  Finding a module does best on one checkpoint and reading a message to the
  assistant on another, so each use may lay a setting of its own over the
  model's, from the settings window or from the command line, and each shows
  what the other kept.

  Scenario: A setting kept for one use in the settings window is asked with when that use asks
    Given a decision model that takes "a mirror maze of shards" to mean a Kaleidoscope only when its model is "typed"
    When the settings window sets the model to "typed" for finding modules alone
    And flyback-cli finds the modules "a mirror maze of shards" describes
    Then the first module found is the Kaleidoscope

  Scenario: A setting kept for one use from the command line is shown in the settings window
    Given a decision model that takes "a mirror maze of shards" to mean a Kaleidoscope only when its model is "typed"
    And flyback-cli sets its model to "typed" for finding modules alone
    Then the settings window shows the model as "typed" for finding modules, and nothing set for every use
