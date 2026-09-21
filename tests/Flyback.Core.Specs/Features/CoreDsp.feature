Feature: Filter, Random, Slew, Drive, Delay and Reverb are the engine's own
  These six lived in the Voice and Effects plugins. Nothing about them needed a
  plugin, so they are built into the engine now: a patch can use them with no
  plugin installed, and a patch saved under one of their old ids still opens
  and plays the same.

  Specified by ADR-0128.

  Scenario: A tone can be filtered and delayed with no plugins installed
    Given a 200 Hz sine through the engine's own filter and delay
    Then the speakers are not silent

  Scenario: A patch saved under the filter's old id still opens and sounds the same
    Given a 200 Hz sine through a filter, saved under the filter's old id "flyback.voice.filter"
    When the file is opened
    Then it opens complete
    And it sounds exactly as it did before it was saved
