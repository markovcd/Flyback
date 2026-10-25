Feature: A Raymarch flies a camera through a 3D scene
  The Picture plugin's Raymarch draws a scene in depth: from each pixel a ray is
  walked into the scene until it touches a surface, and the surface is lit and
  fogged by how far away it is. The camera flies along the scene's open path,
  so a clock on 'travel' is a flight that never crashes, and an envelope on
  'pulse' swells every shape on the beat.

  Scenario Outline: The camera looks down a clear path through every scene
    Given a Raymarch flying through the <scene>
    Then the middle of the screen is sky
    And some of the screen is a surface

    Examples:
      | scene   |
      | lattice |
      | tunnel  |
      | gyroid  |

  Scenario: A pulse swells the shapes so they fill more of the screen
    Given a Raymarch flying through the lattice
    Then the shapes cover more of the screen with the pulse turned up

  Scenario: The lattice repeats, so flying one spacing on is the same view
    Given a Raymarch flying through the lattice
    Then flying on by its spacing shows the same picture
