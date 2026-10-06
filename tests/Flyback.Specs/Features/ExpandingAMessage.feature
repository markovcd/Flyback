Feature: A message to the assistant can be written out before it is sent
  The assistant's column has Expand beside Send. It has the assistant write the
  short message in the box out in full, in the box, to read and edit before it is
  sent. Over a patch it is written as a change to that patch: what makes the patch
  itself, what changes, what stays and how to tell it worked. Over an empty canvas
  it is written as a new patch's brief. Nothing is built until Send.

  Scenario: A change to a patch is written out as a change, in the box
    Given an assistant is set up
    And a sine driven by Time
    When the assistant's column is opened
    And "make it darker" is typed in the assistant's box
    And the message is expanded
    Then the assistant's box holds the assistant's detailed brief
    And the assistant was asked to write out a change to the patch
    And nothing has been sent to build from

  Scenario: Over an empty canvas the message is written out as a new patch
    Given an assistant is set up
    When the assistant's column is opened
    And "a slow dub track about a night train" is typed in the assistant's box
    And the message is expanded
    Then the assistant's box holds the assistant's detailed brief
    And the assistant was asked to write out a new patch
    And nothing has been sent to build from
