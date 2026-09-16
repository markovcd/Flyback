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

        MainWindow.VolumeIsUp(builder.Patch).ShouldBeTrue();
    }

    [Fact]
    public void Nought_is_not_up() =>
        MainWindow.VolumeIsUp(AtVolume(0f)).ShouldBeFalse();

    /// <summary>Nothing below nought either — the range does not go there, but a saved file might.</summary>
    [Fact]
    public void Below_nought_is_not_up() =>
        MainWindow.VolumeIsUp(AtVolume(-1f)).ShouldBeFalse();

    [Fact]
    public void Anything_above_nought_is_up() =>
        MainWindow.VolumeIsUp(AtVolume(0.01f)).ShouldBeTrue();

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

        MainWindow.VolumeIsUp(builder.Patch).ShouldBeTrue();
    }
}
