Feature: Every socket says what it is for
  A module's description says what the module is for and how its sockets work
  together. What one socket does is said on that socket: the panel shows it as
  the tip on the socket's row, and the assistant and the command line read the
  same words after the socket's name when they look a module up. A socket that
  means the same on every module, a position's x or an oscillator's freq, takes
  words described once.

  Scenario: The assistant reads what each socket is for in the panel's words
    When the assistant looks up the Filter
    Then it reads what each of the Filter's sockets is for, word for word

  Scenario: The command line says what each socket is for
    When "flyback-cli modules" describes the Filter
    Then it reads what each of the Filter's sockets is for, word for word

  Scenario: The assistant is told what each module is for and looks up its sockets
    Given every shipped module
    Then the assistant's briefing says what each module is for
    And it says what no socket is for

  Scenario: Every shipped module's words read on their own
    Given every shipped module
    Then each one says what it is for
    And every socket says what it is for
    And everything a module carries says what it is for
    And no socket's help opens with the socket's own name
    And every socket's help is written in sentences
