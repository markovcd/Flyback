using Flyback.App.Audio;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Whether the Output's Volume knob says the speakers should be running — the
/// question the window asks after every recompile, in place of a click on the
/// toggle it replaced (ADR-0079).
/// </summary>
public class VolumeIsUpTests
{
    private static Patch AtVolume(float value)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, value));

        return builder.Patch;
    }

    [Fact]
    public void The_default_volume_is_up()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add(NodeCatalog.OutputTypeId);

        Sound.VolumeIsUp(builder.Patch).ShouldBeTrue();
    }

    [Fact]
    public void Nought_is_not_up() =>
        Sound.VolumeIsUp(AtVolume(0f)).ShouldBeFalse();

    /// <summary>Nothing below nought either — the range does not go there, but a saved file might.</summary>
    [Fact]
    public void Below_nought_is_not_up() =>
        Sound.VolumeIsUp(AtVolume(-1f)).ShouldBeFalse();

    [Fact]
    public void Anything_above_nought_is_up() =>
        Sound.VolumeIsUp(AtVolume(0.01f)).ShouldBeTrue();

    /// <summary>
    /// Wired, there is no default left to read — the row shows "patched" rather
    /// than a slider — so a signal driving it is taken as meant to be heard.
    /// </summary>
    [Fact]
    public void Wired_at_nought_is_still_up()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var source = builder.Add("value");
        var output = builder.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0f));

        builder.Wire(source, 0, output, NodeCatalog.OutputVolumePort);

        Sound.VolumeIsUp(builder.Patch).ShouldBeTrue();
    }

    /// <summary>
    /// Whether the speakers are wanted asks the knob, where Volume is linked
    /// to one.
    /// </summary>
    /// <remarks>
    /// A linked socket keeps its resting number and plays the knob's, and a knob
    /// turning recompiles nothing (ADR-0086), so the question is not asked again
    /// as it crosses nought. Following a knob counts as wired: Volume resting at
    /// nought under a knob turned all the way up has to find the device open.
    /// </remarks>
    [Fact]
    public void Volume_linked_to_a_knob_that_is_up_is_up()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var source = builder.Add("osc.sine");
        var output = builder.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 0f));

        builder.Wire(source, 0, output, NodeCatalog.OutputLeftPort);

        var knob = builder.Patch.AddControl(value: 1f);
        ControlMap.Link(output, NodeCatalog.OutputVolumePort, new ControlLink(knob.Id, 0f, 1f));

        Sound.VolumeIsUp(builder.Patch).ShouldBeTrue("the knob Volume follows is all the way up");
    }
}
