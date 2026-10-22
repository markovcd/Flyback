Feature: Speak: words said in a synthesized voice
  The Voice plugin's Speak says the words typed into it, made from nothing but
  a pulse, a noise and filters. It says them from the start of the patch and
  again every few seconds, or each time its trigger rises, and is silent in
  between. No knob makes it louder than full scale.

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
