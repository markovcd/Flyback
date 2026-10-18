Feature: A file chosen in a page travels with the patch
  A page has no disk, so a sound, a picture or a MIDI file chosen for a module there
  is carried in the patch, as a bundle carries it, and goes with it to the viewer.

  Scenario: A MIDI file chosen in a page plays, and plays in the viewer
    Given a MIDI File with no file chosen
    And the editor is in a page
    And the patch is open in the editor
    When a MIDI file of a note held for half a second is chosen for it from the browser
    Then the MIDI File's panel names "tune.mid"
    When View it is pressed
    And the patch plays from the bundle the viewer was handed
    Then the sound is about 1 at 0.25 seconds
    And the sound is about 0 at 0.75 seconds
