Feature: A plugin can send the picture to another program
  A plugin may offer a picture output: somewhere outside Flyback that the
  picture is sent to, such as VJ software on the same machine or another
  machine on the network. Every frame the patch draws is handed to it, at the
  size it was opened for, with the moment of the patch it shows, so a sound
  sent alongside can be lined up with it. flyback-cli send plays a patch into
  one with no window and says how many frames went.

  Scenario: Every frame the patch draws is sent, each a moment on from the last
    Given rings drawn across the screen, saved as "rings.fbks"
    And a plugin that offers a picture output
    When flyback-cli sends "rings.fbks" to the picture output for 1 second at 10 frames a second
    Then the picture output was sent 10 frames of 64 by 36 pixels
    And each frame shows the patch a tenth of a second after the one before
