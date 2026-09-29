using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Bars;
using Flyback.App.Windows;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The Output's Volume on the toolbar: it shows what the Output holds, a drag along it
/// is one edit, and it is left alone while a wire or a panel knob drives Volume.
/// </summary>
public sealed class VolumeSliderTests : UiTest
{
    private static Slider Slider(MainWindow window) => All<Slider>(window).Single(s => s.Name == "volume");

    private static float Volume(MainWindow window) =>
        Editor(window).History.Patch.Output.InputValues[NodeCatalog.OutputVolumePort];

    /// <summary>Where <paramref name="share"/> of the way along the slider falls, in the window.</summary>
    private static Point At(MainWindow window, double share)
    {
        var slider = Slider(window);
        return slider.TranslatePoint(new Point(slider.Bounds.Width * share, slider.Bounds.Height / 2), window)!.Value;
    }

    private static void Undo(MainWindow window)
    {
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Settle(window);
    }

    [AvaloniaFact]
    public void It_shows_the_Outputs_Volume_and_follows_an_edit_made_elsewhere()
    {
        var window = Open();

        Slider(window).Value.ShouldBe(Volume(window), 0.0001);

        Editor(window).History.Patch.Output.InputValues[NodeCatalog.OutputVolumePort] = 0.8f;
        Editor(window).History.Record();
        Settle(window);

        Slider(window).Value.ShouldBe(0.8, 0.0001);
    }

    [AvaloniaFact]
    public void A_drag_along_it_is_one_edit_to_take_back()
    {
        var window = Open();
        var was = Volume(window);

        window.MouseDown(At(window, 0.9), MouseButton.Left);
        window.MouseMove(At(window, 0.6));
        window.MouseMove(At(window, 0.3));
        window.MouseUp(At(window, 0.3), MouseButton.Left);
        Settle(window);

        Volume(window).ShouldNotBe(was);
        Volume(window).ShouldBe((float)Slider(window).Value, 0.0001f);

        Undo(window);

        Volume(window).ShouldBe(was);
        Slider(window).Value.ShouldBe(was, 0.0001);
    }

    [AvaloniaFact]
    public void A_wire_into_Volume_grays_it_out_and_says_why()
    {
        var builder = new PatchBuilder();
        var level = builder.Add("value");
        var output = builder.Add(NodeCatalog.OutputTypeId);
        builder.Wire(level, 0, output, NodeCatalog.OutputVolumePort);

        var window = Open(builder.Build());

        Slider(window).IsEffectivelyEnabled.ShouldBeFalse();
        ToolTip.GetTip(Slider(window)).ShouldNotBe(VolumeSlider.Tip);
    }
}
