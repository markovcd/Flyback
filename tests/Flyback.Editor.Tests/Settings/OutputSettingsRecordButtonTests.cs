using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Composition;
using Shouldly;

namespace Flyback.Editor.Tests.Settings;

public class OutputSettingsRecordButtonTests : OutputSettingsTest
{
    // --- the record button ---------------------------------------------------

    /// <summary>
    /// On the toolbar, so it is reachable with nothing selected — unlike the
    /// Output panel row it replaced (ADR-0080).
    /// </summary>
    [AvaloniaFact]
    public void The_record_button_is_on_the_toolbar_whatever_is_selected()
    {
        var window = Open();

        Record(window).ShouldNotBeNull();

        Select(window, Editor(window).History.Patch.Output);

        Record(window).ShouldNotBeNull();
    }

    /// <summary>
    /// The light inside the frame is filled rather than outlined: a record light
    /// is a dot, not a stroke, and reads at this size only solid.
    /// </summary>
    [AvaloniaFact]
    public void The_record_glyph_is_a_filled_dot()
    {
        var window = Open();
        var paths = Record(window).GetLogicalDescendants().OfType<Avalonia.Controls.Shapes.Path>().ToList();

        paths.ShouldContain(
            path => path.Data != null && path.Fill != null,
            "the dot is filled, in the button's own foreground");
    }
}
