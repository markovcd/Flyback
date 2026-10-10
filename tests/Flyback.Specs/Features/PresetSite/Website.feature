Feature: The Worker serves the whole website
  The preset site's Worker serves the website's pages beside the presets and
  plugins, so one address has all of it.

  Scenario: The website opens on the overview
    When someone opens the preset site
    Then they read what Flyback is

  Scenario: Every link between the website's pages leads somewhere
    When someone follows every link between the preset site's pages
    Then none of them is missing

  @node
  Scenario: The presets page lists the built-in presets and the shared ones
    Given someone has shared a preset
    When someone opens the preset site's presets page
    Then they can submit a preset there
    And it lists the presets Flyback ships with, marked as built in, beside the shared one
    And it lists the built-in showcases first, then sound and picture, then one idea

  @node
  Scenario: A shared preset a browser cannot play is offered to download
    Given someone has shared a preset that needs a plugin a browser lacks
    When someone opens the preset site's presets page
    Then its card offers it to download, not to play or edit in the browser
    When someone opens the shared preset's page
    Then it offers it to download, not to play or edit in the browser, and says why

  Scenario: The tutorials page plays its videos from one list
    When someone opens the tutorials page
    Then every video in its list has a name of its own to link to, a title and a line on what it shows
    And its player starts on the first video in the list
