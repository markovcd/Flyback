Feature: Module packs
  A module pack is a text whose `module` defs a patch places by id, each as one module,
  sounding as its body written out.

  Scenario: A pack's module is placed as one module and plays as its body
    Given the pack:
      """
      pack ada.echoes 1.0.0

      /// A steady level, halved.
      module half(level = 0.8) = level * 0.5
      """
    When the text is read with the pack:
      """
      ada.echoes.half() |> out.left
      out.volume = 1
      """
    Then the patch holds the one module "ada.echoes.half" besides the Output
    And with the pack the speakers play 0.4

  Scenario: A pack's module offers choices, and the text picks one by name
    Given the pack:
      """
      pack ada.levels 1.0.0

      /// A steady level, at one of two heights.
      module step(level = 0.8 in 0..1, height = "low" in ["low", "high"]) = if height == "high" { level } else { level * 0.5 }
      """
    When the text is read with the pack:
      """
      ada.levels.step(height: "high") |> out.left
      out.volume = 1
      """
    Then with the pack the speakers play 0.8
