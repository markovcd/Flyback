Feature: The assistant can measure what an output carries
  Before wiring something in, the assistant can ask what an output actually
  gives, as numbers, without drawing a picture or rendering a clip.

  Scenario: The assistant measures an LFO nothing is wired to
    Given the assistant has added an LFO turning 3 times a second
    When the assistant measures it
    Then it is told the LFO repeats 3 times a second
