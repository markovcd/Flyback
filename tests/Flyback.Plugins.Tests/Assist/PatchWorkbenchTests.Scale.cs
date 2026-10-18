using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

public partial class PatchWorkbenchTests
{
    // --- a quantiser's scale --------------------------------------------------

    /// <summary>
    /// The other list a node carries, and the same rule: not knobs, but a block
    /// after the call, which edit_patch rewrites like any other text.
    /// </summary>
    [Fact]
    public async Task A_scale_can_be_written_in_one_call()
    {
        var bench = Bench();

        var set = await Write(bench, "let key1 = quantiser() [ C D# F G A# ]");

        set.Ok.ShouldBeTrue(set.Text);
        Scale(bench).ShouldBe([0, 3, 5, 7, 10]);
    }

    /// <summary>Replaced outright, so a smaller scale does not leave the rest of a larger one on.</summary>
    [Fact]
    public async Task Setting_the_scale_replaces_the_whole_of_it()
    {
        var bench = Bench();

        await Write(bench, "let key1 = quantiser() [ C D E F G A B ]");
        (await Edit(bench, "[ C D E F G A B ]", "[ C G ]")).Ok.ShouldBeTrue();

        Scale(bench).ShouldBe([0, 7]);
    }

    /// <summary>
    /// An empty scale is a state worth being able to ask for — the module
    /// passes its signal through — so it is set rather than refused.
    /// </summary>
    [Fact]
    public async Task An_empty_scale_is_allowed()
    {
        var bench = Bench();

        await Write(bench, "let key1 = quantiser() [ C E G ]");
        var set = await Edit(bench, "[ C E G ]", "[ ]");

        set.Ok.ShouldBeTrue(set.Text);
        Scale(bench).ShouldBeEmpty();
    }

    /// <summary>
    /// A scale is neither wiring nor a knob, so without this a Quantiser reads
    /// as a module with nothing set on it — and the names are there because a
    /// row of numbers is not a scale anybody recognizes.
    /// </summary>
    [Fact]
    public async Task Describing_the_patch_shows_the_scale_by_name()
    {
        var drawn = PatchLanguage.Build("let key1 = quantiser() [ C E G ]", NodeCatalog.BuiltIn).Patch;
        var bench = new PatchWorkbench(NodeCatalog.BuiltIn, drawn);

        var told = (await Call(bench, "describe_patch")).Text;

        told.ShouldContain("[ C E G ]");
    }

    [Fact]
    public void The_briefing_says_a_quantiser_carries_a_scale()
    {
        var briefing = Bench().Briefing;

        briefing.ShouldContain("pitch classes are on — not knobs, written as a block of note names after the call");
    }
}
