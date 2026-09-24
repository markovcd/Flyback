Feature: Every socket says what it is for
  A module's description says what the module is for and how its sockets work
  together. What one socket does is said on that socket: the panel shows it as
  the tip on the socket's row, and the assistant and the command line read the
  same words after the socket's name. A socket that means the same on every
  module, a position's x or an oscillator's freq, is described once.

  Scenario: The assistant reads what each socket is for in the panel's words
    When the assistant looks up the Filter
    Then it reads what each of the Filter's sockets is for, word for word

  Scenario: The command line says what each socket is for
    When "flyback-cli modules" describes the Filter
    Then it reads what each of the Filter's sockets is for, word for word

  Scenario: A socket that means the same everywhere is described once
    Given every shipped module
    Then the assistant is told what each standard socket is for once
    And a module tells the assistant about a socket only where it means something of its own

  Scenario: Every shipped module's words read on their own
    Given every shipped module
    Then each one says what it is for
    And no socket's help opens with the socket's own name
    And every socket's help is written in sentences
