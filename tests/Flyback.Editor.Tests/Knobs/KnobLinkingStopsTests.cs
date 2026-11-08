using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Editor.Composition;
using Flyback.Editor.Tests.Controls;
using Flyback.Editor.Windows;
using Shouldly;
using static Flyback.Editor.Tests.Knobs.KnobPanelTests;

namespace Flyback.Editor.Tests.Knobs;

/// <summary>
/// Linking sockets to a knob ends once the canvas it began on is changed under it,
/// put away, or handed to the text, so a later click on a socket is a click again.
/// </summary>
public class KnobLinkingStopsTests : SourceViewTest
{
    /// <summary>Opens the panel and adds a knob, which starts linking sockets to it.</summary>
    private MainWindow Linking(Patch patch)
    {
        var window = Open(patch);

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        Settle(window);
        AddKnob(window);
        Editor(window).Linking.Control.ShouldNotBeNull();

        return window;
    }

    private static void ShouldNotBeLinking(MainWindow window)
    {
        Editor(window).Linking.Control.ShouldBeNull();
        KnobPanelTests.Panel(window).Linking.ShouldBeNull("the knob is not lit once linking stops");
    }

    [AvaloniaFact]
    public void Adding_a_module_stops_linking()
    {
        var window = Linking(Board().Patch);

        Editor(window).Edits.AddNode("value").ShouldNotBeNull();
        Settle(window);

        ShouldNotBeLinking(window);
    }

    [AvaloniaFact]
    public void Removing_a_module_stops_linking()
    {
        var (patch, value) = Board();
        var window = Linking(patch);

        Editor(window).Selection.Select(value.Id);
        Editor(window).Edits.DeleteSelected();
        Settle(window);

        Editor(window).History.Patch.Find(value.Id).ShouldBeNull();
        ShouldNotBeLinking(window);
    }

    [AvaloniaFact]
    public void Going_full_screen_stops_linking()
    {
        var window = Linking(Board().Patch);

        window.KeyPress(Key.F11, RawInputModifiers.None, PhysicalKey.F11, null);
        Settle(window);

        Service<FullScreenPreview>(window).IsFullScreen.ShouldBeTrue();
        ShouldNotBeLinking(window);
    }

    [AvaloniaFact]
    public void Opening_a_patch_stops_linking_even_when_it_has_the_same_knob()
    {
        var window = Linking(Board().Patch);
        var again = PatchIO.Read(PatchIO.ToJson(Editor(window).History.Patch)).Patch;

        Editor(window).History.Open(again);
        Settle(window);

        again.Controls.ShouldNotBeNull().ShouldHaveSingleItem();
        ShouldNotBeLinking(window);
    }

    [AvaloniaFact]
    public void Undo_stops_linking()
    {
        var window = Linking(Board().Patch);
        var history = Editor(window).History;

        Editor(window).Edits.AddNode("value");
        Settle(window);
        KnobPanelTests.Panel(window).Requests!.ToggleLink(history.Patch.Controls!.Single().Id);
        Settle(window);
        Editor(window).Linking.Control.ShouldNotBeNull();

        history.Undo().ShouldBeTrue();
        Settle(window);

        history.Patch.Controls.ShouldNotBeNull().ShouldHaveSingleItem("the knob is still there to link to");
        ShouldNotBeLinking(window);
    }

    [AvaloniaFact]
    public void Showing_the_text_stops_linking()
    {
        var window = Linking(Board().Patch);

        ShowCode(window);

        ShouldNotBeLinking(window);
    }

    [AvaloniaFact]
    public void A_patch_the_text_owns_does_not_start_linking()
    {
        var window = Open();

        Evaluate(window, "sine(freq: 220) |> out.left");
        CodeButton(window).IsChecked = false;
        Settle(window);
        Editor(window).History.Locked.ShouldBeTrue();

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        Settle(window);
        AddKnob(window);

        ShouldNotBeLinking(window);
    }

    [AvaloniaFact]
    public void Learning_a_controller_stops_linking()
    {
        var window = Linking(Board().Patch);
        var knob = Editor(window).History.Patch.Controls!.Single();

        KnobPanelTests.Panel(window).Requests!.Learn(knob.Id, onward: false);
        Settle(window);

        ShouldNotBeLinking(window);
    }
}
