Feature: Windows draws through the graphics card's own OpenGL
  On Windows the window and the picture are drawn through the graphics card's own
  OpenGL, which builds a large patch's shader in about a second, and through Direct3D
  where OpenGL will not start. The settings can ask for Direct3D instead, for a machine
  whose OpenGL misbehaves, from the next time Flyback starts. The editor and the viewer
  read the same setting.

  Scenario: A fresh install draws through OpenGL
    Given no settings have been saved
    When Flyback starts on Windows
    Then it draws through OpenGL
    And it draws through Direct3D where OpenGL will not start

  Scenario: The settings can ask for Direct3D
    Given the settings ask for Direct3D
    When Flyback starts on Windows
    Then it draws through Direct3D
    And it never tries OpenGL
