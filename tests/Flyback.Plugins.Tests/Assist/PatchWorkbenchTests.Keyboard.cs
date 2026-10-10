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
    /// The patch's layout rather than a module's, so it takes no handle, and it
    /// is written out where describe_patch and write_patch both see it.
    /// </summary>
    [Fact]
    public async Task The_keyboard_is_laid_out_for_the_whole_patch()
    {
        var bench = Bench();

        var set = await Call(bench, "set_keyboard", """{"layout":"scale","tonic":9,"scale":"aeolian"}""");

        set.Ok.ShouldBeTrue(set.Text);
        set.Text.ShouldContain("keyboard scale [ A B C D E F G ]");
        bench.Snapshot().Keyboard.ShouldBe(new KeyboardScale(9, "aeolian"));

        (await Call(bench, "set_keyboard", """{"layout":"piano"}""")).Ok.ShouldBeTrue();
        bench.Snapshot().Keyboard.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"layout":"scale"}""")]
    [InlineData("""{"layout":"scale","tonic":2}""")]
    [InlineData("""{"layout":"scale","tonic":12,"scale":"dorian"}""")]
    [InlineData("""{"layout":"scale","tonic":2,"scale":"blues"}""")]
    public async Task A_scale_layout_short_of_a_tonic_and_a_scale_is_refused(string arguments)
    {
        (await Call(Bench(), "set_keyboard", arguments)).Ok.ShouldBeFalse();
    }

    /// <summary>
    /// The length is the patch's rather than a module's, so it takes no handle, and
    /// describe_patch reads it back in the language's own statement.
    /// </summary>
    [Fact]
    public async Task The_patch_length_is_set_for_the_whole_patch()
    {
        var bench = Bench();

        var set = await Call(bench, "set_length", """{"seconds":150.5}""");

        set.Ok.ShouldBeTrue(set.Text);
        set.Text.ShouldContain("2:30.50");
        bench.Snapshot().Length.ShouldBe(150.5);
        bench.Described.ShouldContain("length 2:30.50");
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"seconds":"90"}""")]
    [InlineData("""{"seconds":0.01}""")]
    [InlineData("""{"seconds":90000}""")]
    public async Task A_length_that_is_missing_or_outside_what_a_patch_keeps_is_refused(string arguments)
    {
        var bench = Bench();

        (await Call(bench, "set_length", arguments)).Ok.ShouldBeFalse();
        bench.Snapshot().Length.ShouldBeNull();
    }
}
