Feature: Feedback reads the previous frame
  The camera-pointed-at-its-own-monitor effect. A pixel cannot depend on itself,
  so the frame delay is an explicit module rather than a cycle in the graph —
  and the graph stays acyclic.

  Specified by ADR-0012.

  Scenario: Feedback reads black before any frame has been rendered
    Given a patch containing:
      | name     | module   |
      | previous | feedback |
      | screen   | output   |
    And "previous" output "colour" is wired to "screen" input "colour"
    When the patch is compiled
    Then compilation reports no issues
    And the program contains at least one "SampleFeedback" op
    And the rendered image is entirely black

  # Each frame adds a fixed amount to whatever it read from the last one, so the
  # image can only get brighter if the history is genuinely being carried
  # forward. History is kept in float, which is why three passes land on 0.3
  # rather than drifting.
  Scenario: Each frame accumulates on top of the one before it
    Given a patch containing:
      | name     | module      |
      | previous | feedback    |
      | brighten | colour.gain |
      | screen   | output      |
    And "previous" output "colour" is wired to "brighten" input "colour"
    And "brighten" input "gain" is set to 1
    And "brighten" input "bias" is set to 0.1
    And "brighten" output "colour" is wired to "screen" input "colour"
    When the patch is compiled
    Then rendering 1 frame gives a centre brightness of about 0.1
    And rendering 2 frames gives a centre brightness of about 0.2
    And rendering 3 frames gives a centre brightness of about 0.3

  Scenario: Rewinding clears the accumulated history
    Given a patch containing:
      | name     | module      |
      | previous | feedback    |
      | brighten | colour.gain |
      | screen   | output      |
    And "previous" output "colour" is wired to "brighten" input "colour"
    And "brighten" input "gain" is set to 1
    And "brighten" input "bias" is set to 0.1
    And "brighten" output "colour" is wired to "screen" input "colour"
    When the patch is compiled
    Then rewinding after 5 frames and rendering 1 frame gives a centre brightness of about 0.1
