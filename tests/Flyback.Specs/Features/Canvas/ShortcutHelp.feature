Feature: The shortcuts the website names are in the editor's help
  The website teaches a few keys in passing. Each of them is a row of the editor's
  shortcut help, so what the site promises the help explains.

  Scenario: Every key the website names is a row of the help
    When the keys named on the website are read
    Then each of them is listed in the editor's shortcut help
