Feature: A shared preset's render goes to the preset site through its admin API
  The machine that renders shared presets reaches the site only by asking it: it
  fetches what waits and sends back what it made, and listens for nothing. The page
  never shows half a render, because done is sent last.

  Scenario: A rendered preset is sent its still, its loop and its track, and done last
    Given the preset site is waiting on a preset that makes a picture and a sound
    When flyback-cli render-presets makes a pass with no media folder
    Then the site is sent the preset's still, loop, track and bars
    And it is told the render is done after the rest

  Scenario: A preset that does not open whole is sent as failed, saying why
    Given the preset site is waiting on a preset that names a module nothing here has
    When flyback-cli render-presets makes a pass with no media folder
    Then the site is told the render failed because the patch did not open whole
