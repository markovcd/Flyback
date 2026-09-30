Feature: The preview draws every picture on the GPU
  The shader reads a Scope's or an Analyzer's chart and a Sample's clip as textures,
  so no picture is handed to the processor for what it reads. In a page the processor
  never stands in at all: it is too slow there, so a WebGL that fails is said where
  the picture was.

  Scenario Outline: A picture charted by a Scope stays on the GPU
    Given a preview <where>
    When it is handed a picture charted by a Scope
    Then the preview stays on the GPU

    Examples:
      | where          |
      | in a page      |
      | on the desktop |

  Scenario: A page's WebGL failing leaves the picture out rather than handing it to the processor
    Given a preview in a page
    When its WebGL fails
    Then the preview stays on the GPU

  Scenario: The processor cannot be chosen in a page
    Given a preview in a page
    When the processor is chosen to draw the picture
    Then the preview stays on the GPU
