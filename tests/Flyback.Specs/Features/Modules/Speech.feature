Feature: Speech: words said or sung in a synthesized voice, and a mouth that says them
  The Speech plugin, which the preset site starts with, has two modules. Speak
  says the words typed into it, made from nothing but a pulse, a noise and
  filters. It says them from the start of the patch and again every few seconds,
  or each time its trigger rises, and is silent in between; in Sing it gives each
  syllable a note, in time with its tempo. No knob makes it louder than full
  scale. Mouth draws lips that open and round as a Speak says, on black.

  Scenario: With nothing wired it says its greeting
    Given a Speak with nothing wired
    Then it is heard within the first second

  Scenario: Once the words are said it is silent until they are said again
    Given a Speak saying "Hi." once
    Then it is silent from 1 second to 2

  Scenario: A trigger says the words again
    Given a Speak saying "Hi." once, triggered at 1.5 seconds
    Then it is silent from 1 second to 1.5
    And it is heard from 1.5 seconds to 2

  Scenario: A sound it does not have is skipped with a warning
    Given a Speak saying "[hh ax l q]" once
    Then building it warns about 'q'

  Scenario: Every knob turned all the way up is still inside full scale
    Given a Speak with every knob turned all the way up
    Then its sound never goes past full scale

  # Two beats a note at 120 a minute is a second each.
  Scenario: Sung, each syllable is on its note in time
    Given a Speak singing "la la" to "A3@2 E4@2" at 120 beats a minute
    Then its pitch at 0.5 seconds is 220 Hz
    And its pitch at 1.5 seconds is 330 Hz

  # Half a second a step: A3, a rest, then E4 on the second syllable.
  Scenario: Sung, a rest is silent and takes no syllable
    Given a Speak singing "la la" to "A3 ~ E4" at 120 beats a minute
    Then it is silent from 0.6 second to 0.95
    And its pitch at 1.2 seconds is 330 Hz

  Scenario: A note it cannot read is skipped with a warning
    Given a Speak singing "la la" to "A3 Q9" at 120 beats a minute
    Then building it warns about 'Q9'

  Scenario: A shut mouth is lips, an open one is dark inside
    Given a Mouth open 0
    Then the point 0, 0.06 of the screen is lip red
    And the point 1.5, 0.06 of the screen is black
    Given a Mouth open 1
    Then the point 0, 0 of the screen is dark red
