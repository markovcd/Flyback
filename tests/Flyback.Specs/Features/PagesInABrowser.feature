Feature: The web viewer and the web editor work in a browser
  A page is its WebAssembly build, its scripts, a worker, WebGL and Web Audio together,
  so these open the pages in a real browser, headless, served as the preset site serves
  them. What a script reads off window.flyback is what the page is doing. Each scenario
  takes seconds: the page loads the runtime, interpreted.

  Scenario: The web viewer plays a preset in a browser, its picture drawn and its sound heard
    Given the web viewer is open in a browser on the preset "Sidebands"
    When the web viewer's picture is tapped
    Then the web viewer plays, its sound heard
    And its sound goes straight to the speakers, through no media element
    And the web viewer's picture is drawn

  Scenario: On Android the web viewer's sound goes through a media element, so other playback stops
    Given the web viewer is open in Android's browser on the preset "Sidebands"
    When the web viewer's picture is tapped
    Then the web viewer plays, its sound heard
    And its sound goes through a media element, which takes the phone's audio focus

  Scenario: A browser holds the web viewer's sound back until the page is touched
    Given the web viewer is open in a browser on the preset "Sidebands"
    When a script plays the web viewer
    Then the web viewer plays its picture alone, saying the browser holds the sound back

  Scenario: A tap on the web viewer's playing picture pauses it, and a drag across it does not
    Given the web viewer is open in a browser on the preset "Sidebands"
    When the web viewer's picture is tapped
    Then the web viewer plays, its sound heard
    When a finger drags across the web viewer's picture
    And a mouse drags across the web viewer's picture
    Then the web viewer is still playing
    When the web viewer's picture is tapped
    Then the web viewer is paused

  Scenario: A Line In in the web viewer has the browser's microphone only while it plays
    Given a Line In is patched into the speakers
    And the web viewer is open in a browser on the patch
    When the web viewer's picture is tapped
    Then the web viewer has the browser's microphone open
    When the web viewer's picture is tapped
    Then the web viewer has let the browser's microphone go

  Scenario: The web editor starts in a browser on the preset its address names
    Given the web editor is open in a browser on the preset "Sidebands"
    Then the web editor says it started on "Sidebands", drawn with WebGL
    And the web editor's text has "One sine bending another's phase"

  Scenario: A script edits the patch in the web editor in a browser
    Given a 220 Hz sine is playing
    And the web editor is open in a browser on the patch
    When a script applies the web editor's text with "freq: 220" changed to "freq: 330"
    Then the web editor tells the script nothing is wrong
    And the web editor's text has "freq: 330"
    And the web editor says the patch is edited

  Scenario: A phone's keyboard shrinks the web editor's page rather than covering the field typed into
    Given the web editor is open in a browser on the preset "Sidebands"
    Then the page lets the phone's keyboard shrink it
    And the field the editor types through is big enough that an iPhone does not zoom in on it
