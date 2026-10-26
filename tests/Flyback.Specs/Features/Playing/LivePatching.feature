Feature: A patch can be played live with itself over the picture
  Somebody playing a patch in front of people gives the picture the whole screen and
  keeps patching on top of it: the modules stand over the picture, which shows through
  them, and fade away while nobody touches them, or F2 puts the patch's text there to
  be live-coded. Tab over a full-screen picture puts the modules on or takes them off,
  Shift+F11 goes straight there, and Escape gives the editor back as it was.

  Scenario: Shift+F11 puts the modules over a picture with the whole window
    Given a rainbow across the screen
    And the patch is open in the editor
    When Shift+F11 is pressed
    Then the picture has the whole window
    And the modules stand over the picture

  Scenario: Tab puts the modules over a full-screen picture and takes them off
    Given a rainbow across the screen
    And the patch is open in the editor
    When the picture is given the whole window
    And Tab is pressed over the picture
    Then the modules stand over the picture
    When Tab is pressed over the picture
    Then the picture has the whole window to itself

  Scenario: Escape gives the editor back from live patching
    Given a rainbow across the screen
    And the patch is open in the editor
    When Shift+F11 is pressed
    And Escape is pressed over the picture
    Then the picture does not have the whole window
    And the modules are back on the canvas

  Scenario: A patch can be live-coded as text over the picture
    Given a rainbow across the screen
    And the patch is open in the editor
    When Shift+F11 is pressed
    And F2 is pressed over the picture
    Then the patch's text stands over the picture
    When F2 is pressed over the picture
    Then the modules stand over the picture

  Scenario: Over the picture a module's fill lets it through until one of its values is in hand
    Given a sine on its own
    And the patch is open in the editor
    When Shift+F11 is pressed
    Then the sine's fill lets the picture through
    When the sine's freq well is clicked
    Then the sine's fill is solid
