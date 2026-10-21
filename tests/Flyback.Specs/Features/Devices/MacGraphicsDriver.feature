Feature: macOS draws through OpenGL
  On macOS the picture is drawn by the graphics card through OpenGL, and the window
  through Metal where OpenGL will not start. The editor and the viewer ask for the same.

  Scenario: Flyback starts on macOS through OpenGL
    When Flyback starts on macOS
    Then it draws through OpenGL first
    And it draws through Metal where OpenGL will not start
