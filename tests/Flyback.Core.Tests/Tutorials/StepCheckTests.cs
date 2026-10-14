using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Tutorials;
using Shouldly;

namespace Flyback.Core.Tests.Tutorials;

public class StepCheckTests
{
    private static Patch Patch(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);
        load.Issues.ShouldBeEmpty(load.Report);
        return load.Patch;
    }

    private static StepVerdict Check(string patch, string goal) =>
        StepCheck.Against(Patch(patch), goal, NodeCatalog.BuiltIn);

    [Fact]
    public void A_missing_module_is_asked_for_first() =>
        Check("out.volume = 0.5", "sine() |> out.color").Hint.ShouldBe("Add a Sine.");

    [Fact]
    public void A_missing_wire_names_both_sockets() =>
        Check("let s = sine()", "sine() |> out.color").Hint.ShouldBe("Wire the Sine's out into the Output's color.");

    [Fact]
    public void A_name_ending_in_s_takes_a_bare_apostrophe() =>
        Check("let r = rings()\nlet c = time()", "rings(offset: t) |> out.color").Hint.ShouldBe("Wire the Time's t into the Rings' offset.");

    [Fact]
    public void A_knob_off_its_goal_says_where_it_is() =>
        Check("x |> sine() |> out.color", "x |> sine(freq: 4) |> out.color").Hint.ShouldBe("Turn the Sine's freq to 4; it is at 1.");

    [Fact]
    public void A_knob_within_two_percent_counts() =>
        Check("x |> sine(freq: 4.05) |> out.color", "x |> sine(freq: 4) |> out.color").Done.ShouldBeTrue();

    [Fact]
    public void A_knob_with_a_wire_in_it_asks_for_the_wire_out() =>
        Check("t |> sine(freq: _) |> out.color", "sine(freq: 4) |> out.color")
            .Hint.ShouldBe("Take the wire out of the Sine's freq, then turn it to 4.");

    [Fact]
    public void Anything_else_in_the_patch_is_left_alone() =>
        Check("let a = saw()\nx |> sine() |> out.color\nsaw(freq: 3) |> out.left", "sine() |> out.color")
            .ShouldBe(StepVerdict.Met);

    [Fact]
    public void A_knob_the_goal_leaves_at_its_default_is_not_checked() =>
        Check("sine(freq: 9) |> out.color", "sine() |> out.color").Done.ShouldBeTrue();

    [Fact]
    public void Two_modules_of_a_kind_need_two() =>
        Check("sine() |> out.color", "let a = sine()\nlet b = sine(freq: 2)\na |> out.color").Hint.ShouldBe("Add another Sine.");

    [Fact]
    public void The_pairing_that_meets_the_goal_is_found_among_several()
    {
        var verdict = Check(
            "let a = sine(freq: 2)\nlet b = sine(freq: 4)\nlet c = sine(freq: 8)\nb |> out.color",
            "sine(freq: 4) |> out.color");

        verdict.ShouldBe(StepVerdict.Met);
    }

    [Fact]
    public void A_module_switched_off_is_not_there()
    {
        var patch = Patch("sine() |> out.color");
        patch.Nodes.Single(n => n.TypeId == "osc.sine").Off = true;

        StepCheck.Against(patch, "sine() |> out.color", NodeCatalog.BuiltIn).Hint.ShouldBe("Add a Sine.");
    }

    [Fact]
    public void A_goal_that_does_not_build_is_refused() =>
        Should.Throw<FormatException>(() => Check("sine() |> out.color", "sine( |> out.color"));
}
