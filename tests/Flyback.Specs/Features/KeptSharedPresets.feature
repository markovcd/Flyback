Feature: A shared preset opened once still opens while the preset site is down
  Whatever somebody has opened from the preset site is kept on their machine, with
  everything the site said of it, so the gallery still lists it, stars and all, and
  it still opens when the site does not answer. While the site is up, it opens from
  what was kept rather than being downloaded again.

  Scenario: A shared preset opened before is listed with its stars and opens while the site is down
    Given the preset site shares "Nebula", rated 4.5 by 2 people
    And "Nebula" was opened from the gallery
    When the preset site stops answering
    Then the gallery lists "Nebula" under the preset site, rated "4.5 (2 ratings)"
    And picking "Nebula" there opens it

  Scenario: A shared preset opened before is not downloaded again
    Given the preset site shares "Nebula", rated 4.5 by 2 people
    And "Nebula" was opened from the gallery
    When "Nebula" is opened from the gallery again
    Then "Nebula" was downloaded once
