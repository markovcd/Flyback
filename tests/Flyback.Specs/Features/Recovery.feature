Feature: Work nobody saved outlives a crash
  Each editor keeps what it holds that nobody saved. After a crash the next start
  puts it back, as the document it was and still unsaved, and says so.

  Specified by ADR-0103.

  Scenario: The next start puts back what a crash left
    Given a level of 0.3 shown through a module that halves it
    And Flyback stopped without closing, holding that patch unsaved as "drift"
    When the editor starts again
    Then the editor holds "drift", unsaved
    And the editor says it restored "drift"
    And the screen shows 0.15
