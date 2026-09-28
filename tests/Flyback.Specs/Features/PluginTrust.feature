Feature: A plugin runs only once somebody said yes, and never holds a key
  A folder dropped into plugins/ is a stranger's code until somebody allows it,
  by installing its package or from the command line, and only while its files
  are as they were then. An assistant a plugin offers is handed a way to send,
  never the key it sends with.

  Scenario: A plugin folder copied in by hand does not run until it is allowed
    Given a plugin folder copied in by hand
    When Flyback loads its plugins
    Then the copied plugin does not run
    And it is listed as not yet allowed

  Scenario: A Debug build runs every plugin folder
    Given a plugin folder copied in by hand
    When a Debug build loads its plugins
    Then the copied plugin runs

  Scenario: Allowing a plugin from the command line runs it, until its files change
    Given a plugin folder copied in by hand
    When flyback-cli allows the copied plugin
    And Flyback loads its plugins
    Then the copied plugin runs
    When a file in the copied plugin changes
    And Flyback loads its plugins
    Then the copied plugin does not run
    And it is listed as changed since it was allowed

  Scenario: Every plugin Flyback ships runs with nothing allowed
    When Flyback loads the plugins it was built with, allowing nothing
    Then every one of them runs

  Scenario: An allowed plugin keeps no keys unless it was allowed to
    Given a secret store plugin copied in by hand
    When flyback-cli allows the copied plugin
    And Flyback loads its plugins
    Then its secret store is refused
    When flyback-cli allows the copied plugin to keep keys
    And Flyback loads its plugins
    Then its secret store is offered

  Scenario: Two plugins offering one assistant are both refused it
    When two plugins offer an assistant under the same id
    Then neither is offered
    And both are named as the reason

  Scenario: A key goes only to where it was entered for
    Given a key entered for an assistant that sends to https://api.example.test
    When the assistant sends a request there and one to https://elsewhere.test
    Then only the request to https://api.example.test carries the key
