@browser @hugo
Feature: A patch embedded on someone's page
  embed.js turns each pre data-flyback on a page into highlighted, read-only code
  with Copy and Play. Play opens the web viewer in a frame, over the page or docked
  beside it as the embedder's data-opens says, and a phone always gets the overlay.

  @unmeasured
  Scenario Outline: An embedded patch plays where its embedder said
    Given a page embeds the preset "Sidebands" to open "<opens>"
    Then the embedded code is colored and numbered, with Copy and Play
    When the embedded patch's Play button is clicked
    Then the web viewer opens <where>, playing the embedded patch with its sound heard
    And the embedded patch's button says "Pause"
    When the embedded player's close button is clicked
    Then the player is gone and the embedded patch's button says "Play"

    Examples:
      | opens   | where                 |
      | overlay | over the page         |
      | side    | docked beside the page |

  @unmeasured
  Scenario: A patch embedded to open beside the page opens over it on a phone
    Given a page on a phone embeds the preset "Sidebands" to open "side"
    When the embedded patch's Play button is clicked
    Then the web viewer opens over the page, playing the embedded patch with its sound heard

  @unmeasured
  Scenario: A patch embedded to open in an element of the page plays there
    Given a page embeds the preset "Sidebands" to open in its element "#picture"
    When the embedded patch's Play button is clicked
    Then the web viewer plays in that element, replacing what it held, with its sound heard
    And the embedded patch's button says "Pause"
    When the embedded patch's Pause button is clicked
    Then the embedded patch's button says "Play"
