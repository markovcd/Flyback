Feature: A picture in a page is drawn by the graphics card or not at all
  A Scope's or an Analyzer's chart and a Sample's clip are read on the graphics
  card, so a patch with one draws there like any other. In a page the processor
  never draws the picture: it is too slow there, so a page whose graphics fail
  says so where the picture was.

  Scenario Outline: A picture with a Scope's chart in it is drawn by the graphics card
    Given a preview <where>
    When it is handed a picture with a Scope's chart in it
    Then the graphics card draws the picture

    Examples:
      | where          |
      | in a page      |
      | on the desktop |

  Scenario: A page whose graphics fail leaves the picture out rather than drawing it slowly
    Given a preview in a page
    When the page's graphics fail
    Then the processor does not take the picture over

  Scenario: The processor cannot be chosen in a page
    Given a preview in a page
    When the processor is chosen to draw the picture
    Then the processor does not take the picture over
