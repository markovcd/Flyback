Feature: A plugin package says what it is before anybody runs it
  Whoever decides about a package, a person publishing it on the preset site or a
  script reviewing it, asks flyback-cli plugin describe what it holds. The answer
  is what the install dialog would show, and a package the editor would refuse is
  refused.

  Scenario: A package says what it adds and who signed it
    Given a plugin package signed by its author
    When flyback-cli describes the package
    Then the command succeeds
    And it names the modules the package declares
    And it names the key that signed it

  Scenario: A package changed after it was signed is refused
    Given a plugin package changed after it was signed
    When flyback-cli describes the package
    Then the command says the package is refused
