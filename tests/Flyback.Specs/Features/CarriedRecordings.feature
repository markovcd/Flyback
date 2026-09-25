Feature: A preset carries the recordings it plays
  A preset may speak. The sound files it plays ship inside its plugin, so it
  opens and plays on a machine that has never had them.

  Scenario: Mycelium speaks with none of its recordings on the disk
    Given the shipped preset "Mycelium"
    And none of the recordings it plays is on this machine
    Then it opens with nothing wrong
