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
    // --- looking ------------------------------------------------------------

    /// <summary>
    /// The patch comes back in the language, under the handles the editing tools
    /// answer to — so what is read here can be pointed at by set_knobs, and
    /// written back whole by write_patch, without translating between two
    /// notations.
    /// </summary>
    [Fact]
    public async Task Describing_the_patch_names_handles_wires_and_knobs()
    {
        var told = await Call(await Lit(0.25f), "describe_patch");

        told.Text.ShouldContain("let knob1 = value(");
        told.Text.ShouldContain("0.25");
        told.Text.ShouldContain("knob1 |> out.color");
    }

    /// <summary>
    /// A warning names its module by handle, which the compiler's own wording cannot:
    /// told only "the Output", a model tries to add one.
    /// </summary>
    [Fact]
    public async Task A_warning_names_its_module_by_handle()
    {
        var bench = Bench();

        var added = await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"tone1"}""");
        var proposed = await Call(bench, "propose", """{"summary":"a tone"}""");

        added.Text.ShouldContain("output1: Nothing is wired into the Output");
        proposed.Text.ShouldContain("into output1's 'color'");
    }

    /// <summary>
    /// A warning is something to know, not a reason to refuse: the frames come back
    /// with the warning in the caption.
    /// </summary>
    [Fact]
    public async Task A_patch_with_only_warnings_is_still_rendered()
    {
        var bench = Bench();

        // A wave swinging below zero into a socket that takes nothing below it is a warning.
        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"wobble1"}""");
        await Call(bench, "add_module", """{"type_id":"osc.sine","handle":"tone1"}""");
        await Call(bench, "connect", """{"from":"tone1","to":"output1","to_port":"color"}""");
        var wired = await Call(bench, "connect", """{"from":"wobble1","to":"tone1","to_port":"freq"}""");

        wired.Text.ShouldContain("Worth knowing");

        var looked = await Call(bench, "render");

        looked.Ok.ShouldBeTrue(looked.Text);
        looked.Png.ShouldNotBeNull();
        looked.Text.ShouldContain("warned");
    }

    [Fact]
    public async Task A_render_is_a_png_of_the_size_it_says()
    {
        var looked = await Call(await Lit(0.5f), "render", """{"times":[0.5,1.5]}""");

        looked.Ok.ShouldBeTrue(looked.Text);
        looked.Png.ShouldNotBeNull();

        Signature(looked.Png).ShouldBe("PNG");
        Width(looked.Png).ShouldBe(320 * 2);
        Height(looked.Png).ShouldBe(180);
    }

    [Fact]
    public async Task A_render_can_start_anywhere_and_only_warms_up_just_before_it()
    {
        var bench = await Lit(0.5f);

        var late = await Call(bench, "render", """{"from":3000,"times":[0.5,1]}""");

        late.Ok.ShouldBeTrue(late.Text);
        late.Text.ShouldContain("at 3000.5s, 3001s");
        late.Text.ShouldContain("warmed from 2999s");

        var early = await Call(bench, "render", """{"times":[0.5]}""");

        early.Text.ShouldContain("warmed from zero");
    }

    /// <summary>
    /// A patch for the speakers draws nothing, and the compiler does not remark
    /// on it — that is the point of it. Rendering one anyway would hand back a
    /// black rectangle, which is the one thing an assistant must never be shown
    /// for a patch that is working.
    /// </summary>
    [Fact]
    public async Task A_patch_with_no_screen_is_not_rendered_black_at_it()
    {
        var looked = await Call(await Heard(), "render");

        looked.Ok.ShouldBeFalse();
        looked.Png.ShouldBeNull();
        looked.Text.ShouldContain("'color'");
    }

    /// <summary>
    /// The regression this exists for: the renderer owns the history that
    /// <c>feedback</c> reads, so a render that jumped straight to its target time
    /// would hand back the same black frame whatever time was asked for — and an
    /// assistant shown black would go and "fix" a patch that was working.
    /// </summary>
    [Fact]
    public async Task A_feedback_patch_looks_different_once_it_has_been_warmed()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"feedback","handle":"previous1"}""");
        await Call(bench, "add_module", """
            {"type_id":"color.gain","handle":"gain1","knobs":[{"port":"gain","value":1},{"port":"bias","value":0.1}]}
            """);

        await Call(bench, "connect", """{"from":"previous1","to":"gain1","to_port":"color"}""");
        var wired = await Call(bench, "connect", """{"from":"gain1","to":"output1","to_port":"color"}""");
        wired.Ok.ShouldBeTrue(wired.Text);

        var cold = await Call(bench, "render", """{"times":[0]}""");
        var warm = await Call(bench, "render", """{"times":[1.5]}""");

        cold.Png.ShouldNotBeNull();
        warm.Png.ShouldNotBeNull();
        warm.Png.ShouldNotBe(cold.Png);
    }

    [Fact]
    public async Task A_patch_that_does_not_compile_cannot_be_looked_at()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine"}""");
        var looked = await Call(bench, "render");

        looked.Ok.ShouldBeFalse();
        looked.Png.ShouldBeNull();
    }
}
