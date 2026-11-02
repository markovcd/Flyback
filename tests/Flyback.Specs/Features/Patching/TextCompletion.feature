Feature: The text view offers what may be typed where the caret is, and says what is written
  Somebody writing a patch as text is offered the words that fit where they are
  typing: the sockets of the module whose brackets they are in, the outputs after
  a dot, the names in sight and the modules there are. A new name or a number is
  theirs to type, so nothing comes up for one. Ctrl+Space asks for the list
  anywhere. Inside a call its sockets stand above the caret, the one being filled
  marked, and resting the pointer on a word says what it is. flyback-cli complete
  and flyback-cli hover give the same to a script.

  Scenario: Opening a module's brackets offers the sockets it takes
    Given the text view is open and empty
    When "sine(" is typed into the text
    Then the list under the caret offers "freq"
    And the list under the caret offers "amp"

  Scenario: Choosing a socket writes it ready for its value
    Given the text view is open and empty
    When "sine(fr" is typed into the text
    And what the list has picked is chosen
    Then the text reads "sine(freq: "

  Scenario: The Output's sockets are offered after its dot
    Given the text view is open and empty
    When "x |> out." is typed into the text
    Then the list under the caret offers "color"

  Scenario: A new name brings nothing up
    Given the text view is open and empty
    When "let wob" is typed into the text
    Then nothing is offered

  Scenario: Inside a call its sockets show above the caret, the one being filled marked
    Given the text view is open and empty
    When "x |> smoothstep(0.1, " is typed into the text
    Then the sockets above the caret mark "edge1" as the one being filled

  Scenario: Resting on a word says what it is
    Given the text view is open and empty
    When "sine(freq: 2) |> out.left" is typed into the text
    Then resting on "sine" says "sine · Sine (osc.sine)"
    And resting on "freq" says "freq · input of Sine · 0 to 20000"
