Feature: A script edits the patch in a page as text
  A page's window.flyback reads the open patch in the language and applies text to
  it as the text view's Apply does: one edit, which one undo takes back.

  Scenario: Text a script applies is an edit, and undo takes it back
    Given a 220 Hz sine is playing
    And the editor is in a page
    And the patch is open in the editor
    When a script applies the page's text with "freq: 220" changed to "freq: 330"
    Then the script is told nothing is wrong
    And the page's text has "freq: 330"
    And the sine is at 330 Hz
    When that is undone
    Then the sine is at 220 Hz

  Scenario: Text that does not read is answered with what is wrong, and changes nothing
    Given a 220 Hz sine is playing
    And the editor is in a page
    And the patch is open in the editor
    When a script applies the text "kaleidoscop() |> out.color"
    Then the script is told what is wrong on line 1
    And the sine is at 220 Hz
    And there is nothing to undo
