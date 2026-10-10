Feature: A shared preset's render goes to the preset site through its admin API
  The machine that renders shared presets reaches the site only by asking it: it
  fetches what waits and sends back what it made, and listens for nothing. The page
  never shows half a render, because done is sent last.

  Scenario: A rendered preset is sent its still, its loop and its track, and done last
    Given the preset site is waiting on a preset that makes a picture and a sound
    When flyback-cli render-presets makes a pass with no media folder
    Then the site is sent the preset's still, loop, track and bars
    And it is told the render is done after the rest

  Scenario: A render of the still alone sends no loop and no track
    Given the preset site is waiting on a preset that makes a picture and a sound
    When flyback-cli render-presets makes a pass with no media folder, the still alone
    Then the site is sent the preset's still and done, and nothing else

  Scenario: A render made apart from the site is sent with done last
    Given a folder where render-presets left a finished render and an unfinished one
    When flyback-site sends the folder to the preset site
    Then the site is sent the finished render's files, and done after them
    And nothing of the unfinished render is sent

  Scenario: A preset that does not open whole is sent as failed, saying why
    Given the preset site is waiting on a preset that names a module nothing here has
    When flyback-cli render-presets makes a pass with no media folder
    Then the site is told the render failed because the patch did not open whole
