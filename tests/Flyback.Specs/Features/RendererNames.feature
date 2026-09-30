Feature: What draws the picture is named for what it is
  The status bar, the full-screen stats line and the Renderer box in the settings
  name what draws the picture: the CPU, or the API the graphics card is driven
  through. Direct3D on Windows is ANGLE translating OpenGL ES, and is named as
  Direct3D rather than as the OpenGL ES it looks like from inside.

  Scenario Outline: A graphics context is named for the API under it
    Given a graphics context that is <profile> and whose renderer says "<renderer>"
    Then the picture is said to be drawn through <name>

    Examples:
      | profile   | renderer                                                                      | name      |
      | desktop   | NVIDIA GeForce RTX 4070 SUPER/PCIe/SSE2                                       | OpenGL    |
      | embedded  | ANGLE (NVIDIA, NVIDIA GeForce RTX 4070 SUPER Direct3D11 vs_5_0 ps_5_0, D3D11) | Direct3D  |
      | embedded  | ANGLE (Intel, Vulkan 1.3.0 (Intel(R) UHD Graphics 620))                        | Vulkan    |
      | embedded  | Mesa Intel(R) UHD Graphics 620                                                | OpenGL ES |

  Scenario: The CPU is named as the CPU
    Given the picture is drawn on the processor
    Then the picture is said to be drawn through CPU
