using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Editor.Bars;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Ui;

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
        var builder = new PatchBuilder();
        builder.Wire(builder.Add("value"), 0, builder.Add(NodeCatalog.OutputTypeId), NodeCatalog.OutputLeftPort);

        var window = Open(builder.Build());
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
    public void A_patch_with_no_sound_has_no_Volume_and_one_wired_to_sound_has()
    {
        var builder = new PatchBuilder();
        var level = builder.Add("value");
        var output = builder.Add(NodeCatalog.OutputTypeId);
        builder.Wire(level, 0, output, NodeCatalog.OutputColorPort);

        var window = Open(builder.Build());

        Slider(window).IsEffectivelyVisible.ShouldBeFalse();

        Editor(window).History.Patch.Connect(level.Id, 0, output.Id, NodeCatalog.OutputLeftPort);
        Editor(window).History.Record();
        Settle(window);

        Slider(window).IsEffectivelyVisible.ShouldBeTrue();
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

    [AvaloniaFact]
    public void A_notch_of_the_wheel_over_it_turns_it_and_is_taken_back_like_any_edit()
    {
        var builder = new PatchBuilder();
        builder.Wire(builder.Add("value"), 0, builder.Add(NodeCatalog.OutputTypeId), NodeCatalog.OutputLeftPort);

        var window = Open(builder.Build());
        var slider = Slider(window);
        var was = Volume(window);

        window.MouseWheel(At(window, 0.5), new Vector(0, -1));
        Settle(window);

        Volume(window).ShouldBe((float)(was - VolumeSlider.WheelStep * (slider.Maximum - slider.Minimum)), 0.0001f);

        Undo(window);

        Volume(window).ShouldBe(was);
    }
}
