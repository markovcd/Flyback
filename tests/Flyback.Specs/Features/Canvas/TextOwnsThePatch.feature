Feature: The canvas says when the text owns the patch
  While the text is the document, the canvas is drawn from it: values still turn in
  their wells, but modules and wires are added and removed in the text. A tag in the
  canvas's corner says so, and pressing it opens the text. While the canvas owns the
  patch there is no tag.

  Specified by ADR-0068.

  Scenario: A canvas that owns its patch shows no tag
    Given a sine on its own
    And the patch is open in the editor
    Then the canvas's corner says nothing about who owns the patch

  Scenario: The canvas built from the text says so in its corner
    Given the text is the document
    When the canvas is shown
    Then the canvas's corner says "Text owns this patch"

  Scenario: Pressing the tag opens the text
    Given the text is the document
    And the canvas is shown
    When the tag in the canvas's corner is pressed
    Then the text view is showing
