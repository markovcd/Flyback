Feature: A submission is read with the app's own readers before the preset site lists it
  The preset site takes a file at once and lists it once flyback-site has read it the
  way Flyback opens files, without running anything in it. What it says of a preset is
  what the patch says of itself, and what a browser would lack to open it.

  Scenario: A patch is taken with what it says about itself
    Given a submitted patch by "Ada", described as "A slow drone." and tagged "ambient"
    When flyback-site checks the submission
    Then it is taken, by "Ada", described as "A slow drone." and tagged "ambient"

  Scenario: A patch built on a plugin a browser lacks says which
    Given a submitted patch built on the "Lantern" plugin
    When flyback-site checks the submission
    Then it is taken, saying "Needs the Lantern plugin"

  Scenario: A file that is not a patch is refused, saying why
    Given a submitted file that is not a patch
    When flyback-site checks the submission
    Then it is refused because "That is not a Flyback patch. Send a .fbk or .fbkb file."
