Feature: A patch from somebody else reaches only its own files
  A patch or a bundle is often somebody else's file. Opening, playing and saving
  one never reaches another machine, never writes outside the folder it is saved
  to, and never packs anything but the sounds and pictures it plays.

  Scenario: A bundle never carries a file that is not a sound or a picture
    Given a picture module pointed at a private key on this machine
    When the patch is saved as a bundle
    Then the bundle carries nothing, and says the key could not be read

  Scenario: A bundle cannot name a file outside its own folder
    Given a bundle holding a file named to climb out of its folder
    When the bundle is opened
    Then it carries only the files it packed

  Scenario: A patch never reaches for a picture on another machine
    Given a picture module pointed at a picture on another machine
    Then the picture is not looked for, because it is on another machine
