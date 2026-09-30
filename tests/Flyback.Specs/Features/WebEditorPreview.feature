Feature: The web editor's preview never draws on the processor
  A page draws the picture on WebGL alone: the processor is too slow there to keep
  up. What WebGL cannot draw, a Scope's or an Analyzer's chart or a Sample, is left
  out, and the preview says why. The desktop editor hands it to the processor.

  Scenario: A picture only the processor can draw is left out in a page, saying why
    Given a preview in a page
    When it is handed a picture charted by a Scope
    Then the preview stays on WebGL
    And it says it cannot draw a Scope

  Scenario: A page's WebGL failing leaves the picture out rather than handing it to the processor
    Given a preview in a page
    When its WebGL fails
    Then the preview stays on WebGL

  Scenario: The processor cannot be chosen in a page
    Given a preview in a page
    When the processor is chosen to draw the picture
    Then the preview stays on WebGL

  Scenario: The desktop editor still draws on the processor what its shader cannot
    Given a preview on the desktop
    When it is handed a picture charted by a Scope
    Then the preview draws on the processor
