Feature: The preset site serves the whole website
  The website on GitHub Pages is the static part. The preset site serves the
  same pages beside its presets and plugins, so one address has all of it.

  Scenario: The preset site opens on the overview
    When someone opens the preset site
    Then they read what Flyback is

  Scenario: Every link between the website's pages leads somewhere on the preset site
    When someone follows every link between the preset site's pages
    Then none of them is missing
