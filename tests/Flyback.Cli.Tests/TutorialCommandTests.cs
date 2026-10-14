using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Tutorials;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

public class TutorialCommandTests
{
    private static Tutorial FirstPatch => ShippedTutorials.Find("first-patch")!;

    private static Patch Built(string source) => PatchLanguage.Build(source, NodeCatalog.BuiltIn).Patch;

    private static (int Code, string Out, string Error) Check(string source, int? step = null, bool json = false)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = TutorialCommand.Check(FirstPatch, Built(source), NodeCatalog.BuiltIn, step, json, output, error);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void A_patch_short_of_a_step_is_a_problem_and_says_what_is_missing()
    {
        var (code, text, _) = Check("let wave = sine()");

        code.ShouldBe(Exit.Problems);
        text.ShouldContain("✓ 1. Add a Sine");
        text.ShouldContain("✗ 2. Wire it into the color  Wire the Sine's out into the Output's color.");
    }

    [Fact]
    public void One_step_met_is_all_right() =>
        Check("let wave = sine()", step: 1).Code.ShouldBe(Exit.Ok);

    [Fact]
    public void A_step_past_the_end_is_refused() =>
        Check("let wave = sine()", step: 99).Code.ShouldBe(Exit.Failed);

    [Fact]
    public void The_json_says_each_step_done_or_not_with_its_hint()
    {
        var (_, text, _) = Check("let wave = sine()", json: true);

        text.ShouldContain("\"done\": true");
        text.ShouldContain("\"hint\": \"Wire the Sine's out into the Output's color.\"");
    }

    [Fact]
    public void The_list_names_every_tutorial()
    {
        var output = new StringWriter();

        TutorialCommand.List(json: false, output).ShouldBe(Exit.Ok);
        foreach (var id in ShippedTutorials.Order) output.ToString().ShouldContain(id);
    }
}
