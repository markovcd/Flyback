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
    // --- the computer keyboard ------------------------------------------------

    /// <summary>
    /// The patch's layout rather than a module's, written on a line of its own where
    /// describe_patch and edit_patch both see it.
    /// </summary>
    [Fact]
    public async Task The_keyboard_is_laid_out_for_the_whole_patch()
    {
        var bench = await Lit();

        var set = await Edit(bench, "knob1 |> out.color", "knob1 |> out.color\nkeyboard scale [ A B C D E F G ]");

        set.Ok.ShouldBeTrue(set.Text);
        bench.Snapshot().Keyboard.ShouldBe(new KeyboardScale(9, "aeolian"));
        bench.Described.ShouldContain("keyboard scale [ A B C D E F G ]");

        (await Edit(bench, "\nkeyboard scale [ A B C D E F G ]", "")).Ok.ShouldBeTrue();
        bench.Snapshot().Keyboard.ShouldBeNull();
    }

    /// <summary>
    /// The length is the patch's rather than a module's, and describe_patch reads it
    /// back in the language's own statement.
    /// </summary>
    [Fact]
    public async Task The_patch_length_is_set_for_the_whole_patch()
    {
        var bench = await Lit();

        var set = await Edit(bench, "knob1 |> out.color", "knob1 |> out.color\nlength 2:30.50");

        set.Ok.ShouldBeTrue(set.Text);
        bench.Snapshot().Length.ShouldBe(150.5);
        bench.Described.ShouldContain("length 2:30.50");
    }

    [Theory]
    [InlineData("length 0.01")]
    [InlineData("length 90000")]
    public async Task A_length_outside_what_a_patch_keeps_is_refused(string line)
    {
        var bench = await Lit();

        (await Edit(bench, "knob1 |> out.color", "knob1 |> out.color\n" + line)).Ok.ShouldBeFalse();
        bench.Snapshot().Length.ShouldBeNull();
    }
}
