Feature: What draws the picture is named for what it is
  The status bar, the full-screen stats line and the Renderer box in the settings
  name what draws the picture: the CPU, or the API the graphics card is driven
  through. Direct3D on Windows is ANGLE translating OpenGL ES, and is named as
  Direct3D rather than as the OpenGL ES it looks like from inside.

  Scenario Outline: A graphics card is named for the API it is driven through
    Given a graphics card driven through <driver>
    Then the picture is said to be drawn through <name>

    Examples:
      | driver                          | name      |
      | desktop OpenGL                  | OpenGL    |
      | ANGLE over Direct3D 11          | Direct3D  |
      | ANGLE over Vulkan               | Vulkan    |
      | OpenGL ES, straight from Mesa   | OpenGL ES |

  Scenario: The CPU is named as the CPU
    Given the picture is drawn on the processor
    Then the picture is said to be drawn through CPU
