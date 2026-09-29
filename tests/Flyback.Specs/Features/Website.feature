Feature: The preset site serves the whole website
  The website on GitHub Pages is the static part. The preset site serves the
  same pages beside its presets and plugins, so one address has all of it.

  Scenario: The preset site opens on the overview
    When someone opens the preset site
    Then they read what Flyback is

  Scenario: Every link between the website's pages leads somewhere on the preset site
    When someone follows every link between the preset site's pages
    Then none of them is missing

  Scenario: The preset site's presets page is the GitHub Pages one, with the shared presets too
    When someone opens the preset site's presets page
    Then they can submit a preset there
    And it lists the presets Flyback ships with, marked as built in, beside the shared ones
    And it lists the built-in showcases first, then sound and picture, then one idea
