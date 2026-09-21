Feature: A frequency knob reaches from a slow wobble to the top of hearing
  An oscillator dropped on the canvas and patched to the speakers can be turned
  up into a tone by its own knob. The knob sweeps in decades, so the rates a
  picture or an LFO wants get as much of its travel as the pitches do.

  Scenario Outline: An oscillator's knob covers LFO rates and audio pitches
    Given a <module> from the catalogue
    Then its frequency knob turns from standing still to 20 kHz
    And the lower half of its travel is slower than 20 Hz
    And the upper half of its travel is audible

    Examples:
      | module   |
      | Sine     |
      | Saw      |
      | Triangle |
      | Square   |
      | Pulse    |

