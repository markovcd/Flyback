Feature: A patch's likeliest problem is said first
  With a decision model chosen, the complaints about a patch that is silent or
  dark are put in order of how likely each is to be why, so the one to fix
  first is the one read first.

  Scenario: The likeliest cause is the first complaint
    Given a decision model that blames the module "osc.nothere"
    When flyback-cli checks a patch missing two modules, likeliest first
    Then the first complaint is about "osc.nothere"
