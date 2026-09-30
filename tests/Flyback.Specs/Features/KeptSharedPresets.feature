Feature: A shared preset opened once still opens while the preset site is down
  Whatever somebody has opened from the preset site is kept on their machine, with
  everything the site said of it, so the gallery still lists it, stars and all, and
  it still opens when the site does not answer.

  Scenario: A shared preset opened before is listed with its stars and opens while the site is down
    Given the preset site shares "Nebula", rated 4.5 by 2 people
    And "Nebula" was opened from the gallery
    When the preset site stops answering
    Then the gallery lists "Nebula" under the preset site, rated "4.5 (2 ratings)"
    And picking "Nebula" there opens it

  Scenario: A shared preset taken off the site does not open from what was kept
    Given the preset site shares "Nebula", rated 4.5 by 2 people
    And "Nebula" was opened from the gallery
    And the gallery lists "Nebula" under the preset site
    When "Nebula" is taken off the preset site
    And "Nebula" is picked there
    Then the editor says "“Nebula” has been taken off the preset site."
    And while the preset site does not answer, the gallery lists nothing kept from it
