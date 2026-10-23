using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Flyback.Editor.Composition;
using Flyback.Engine.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Settings;

public partial class OutputSettingsTests
{
    // --- the rewind button -----------------------------------------------

    /// <summary>
    /// Named rather than found by content, same as record: it lives on the
    /// toolbar now and a glyph has nothing a test can read.
    /// </summary>
    private static Button Rewind(MainWindow window) => Named<Button>(window, "rewind");

    /// <summary>
    /// On the toolbar, so it is reachable with nothing selected — unlike the
    /// Output panel row it replaced (ADR-0081).
    /// </summary>
    [AvaloniaFact]
    public void The_rewind_button_is_on_the_toolbar_whatever_is_selected()
    {
        var window = Open();

        Rewind(window).ShouldNotBeNull();

        Select(window, Editor(window).History.Patch.Output);

        Rewind(window).ShouldNotBeNull();
    }

    /// <summary>
    /// Filled rather than outlined, like record beside it — a bar and a
    /// triangle read at this size only solid.
    /// </summary>
    [AvaloniaFact]
    public void The_rewind_glyph_is_filled_rather_than_stroked()
    {
        var window = Open();
        var icon = Rewind(window).Content.ShouldBeOfType<Avalonia.Controls.Shapes.Path>();

        icon.Data.ShouldNotBeNull();
        icon.Fill.ShouldNotBeNull("the fill follows the button's own foreground");
    }

    /// <summary>The preset it opens on draws something, so there is a take to record.</summary>
    [AvaloniaFact]
    public void The_record_button_is_offered_when_the_patch_reaches_something()
    {
        var window = Open();
        Select(window, Editor(window).History.Patch.Output);

        Record(window).IsEnabled.ShouldBeTrue();
    }

    /// <summary>
    /// Nothing wired into either half of the Output means nothing to record, and
    /// the button says so by being grayed rather than by opening a dialog with
    /// an empty list of file types.
    /// </summary>
    [AvaloniaFact]
    public void The_record_button_is_grayed_out_when_the_patch_reaches_nothing()
    {
        var window = Open();
        var editor = Editor(window);

        editor.History.Open(Presets.Empty(NodeCatalog.BuiltIn));
        Select(window, editor.History.Patch.Output);

        Record(window).IsEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// And it comes back the moment something reaches it — the state follows the
    /// patch rather than being decided once when the panel was built.
    /// </summary>
    [AvaloniaFact]
    public void Wiring_something_up_brings_the_record_button_back()
    {
        var window = Open();
        var editor = Editor(window);

        editor.History.Open(Presets.Empty(NodeCatalog.BuiltIn));
        Select(window, editor.History.Patch.Output);
        Record(window).IsEnabled.ShouldBeFalse();

        var knob = editor.Edits.AddNode("value");
        knob.ShouldNotBeNull();
        editor.History.Patch.Connect(knob.Id, 0, editor.History.Patch.Output.Id, NodeCatalog.OutputColorPort);
        editor.History.Record();

        Select(window, editor.History.Patch.Output);

        Record(window).IsEnabled.ShouldBeTrue();
    }
}
