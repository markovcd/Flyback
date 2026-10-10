Feature: Modules move into and out of a group after it is made
  A group is not fixed once made. A module picked from the list over an open
  group lands in it, carrying modules with Shift held puts them in the group
  they are let go over or takes them out of their own, and grouping a group
  together with loose modules adds them to it under the name it has.

  Background:
    Given an open group "Voice" of three modules in a row
    And a Time below it, in no group

  Scenario: A module picked from the list over an open group joins it
    When the list is opened inside "Voice"
    And "Sine" is picked from the list that opens
    Then "Voice" holds 4 modules

  Scenario: A module carried onto an open group with Shift held joins it
    When the Time is carried into "Voice" with Shift held
    Then "Voice" holds the Time

  Scenario: A module carried without Shift only moves
    When the Time is carried into "Voice"
    Then "Voice" does not hold the Time

  Scenario: A module carried off its group with Shift held leaves it
    When the middle module is carried out of "Voice" with Shift held
    Then "Voice" holds 2 modules
    And the middle module is in no group

  Scenario: Grouping a group with a loose module keeps the group's name
    When "Voice" and the Time are grouped
    Then the patch has a shut group "Voice" of 4 modules

  Scenario: Grouping groups of which only one has a name keeps that name
    Given a group of two modules with no name
    When "Voice", the group with no name and the Time are grouped
    Then the patch has a shut group "Voice" of 6 modules

  Scenario: A socket on a box's edge is named for itself while a wire is on it
    Given the Time feeds an Expression drawn in a box with a Multiply
    Then the box's edge reads "Expression.a" where the Time's wire arrives
