using Avalonia.Controls;
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
    /// Filled rather than outlined, unlike the other drawn icons — a record
    /// light is a dot, not a stroke, and reads at this size only solid.
    /// </summary>
    [AvaloniaFact]
    public void The_record_glyph_is_filled_rather_than_stroked()
    {
        var window = Open();
        var icon = Record(window).Content.ShouldBeOfType<Avalonia.Controls.Shapes.Path>();

        icon.Data.ShouldNotBeNull();
        icon.Fill.ShouldNotBeNull("the fill follows the button's own foreground");
    }
}
