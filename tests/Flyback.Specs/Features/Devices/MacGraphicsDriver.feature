Feature: macOS draws the picture through Metal
  On macOS the window is drawn through OpenGL, and through Metal where OpenGL will not
  start. The picture is drawn through Metal, which builds a large patch's shader without
  holding the window; the settings can ask for OpenGL instead, to compare the two, and
  either takes over at once. The editor and the viewer ask for the same.

  Scenario: Flyback starts on macOS through OpenGL
    When Flyback starts on macOS
    Then it draws through OpenGL first
    And it draws through Metal where OpenGL will not start

  Scenario: A fresh install draws the picture through Metal
    Given no settings have been saved
    When a Mac draws the picture
    Then the picture is drawn through "Metal"

  Scenario: The settings can ask for the picture through OpenGL
    Given the settings ask the Mac for OpenGL
    When a Mac draws the picture
    Then the picture is drawn through "OpenGL"
