Feature: The text view offers what may be typed where the caret is
  Somebody writing a patch as text is offered the words that fit where they are
  typing: the sockets of the module whose brackets they are in, the outputs after
  a dot, the names in sight and the modules there are. A new name or a number is
  theirs to type, so nothing comes up for one. Ctrl+Space asks for the list
  anywhere, and flyback-cli complete gives the same list to a script.

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
