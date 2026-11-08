Feature: A heavy sound shares the machine's cores
  A heavy patch's sound is shared across several cores, half the machine's up to four
  unless Settings → Sound's Cores says otherwise. Every flyback-cli command takes it from
  the editor's settings, and a render's --sound-cores overrides it for one run. The sound
  is the same, sample for sample, on any number of cores.

  Scenario: A render is the same sound on one core as on several
    Given the editor's settings share the sound across 4 cores
    When flyback-cli renders the preset "Warehouse" as "shared.wav" for 0.5 seconds, --oversample 2
    And flyback-cli renders the preset "Warehouse" as "alone.wav" for 0.5 seconds, --sound-cores 1
    Then "shared.wav" and "alone.wav" hold the same samples
