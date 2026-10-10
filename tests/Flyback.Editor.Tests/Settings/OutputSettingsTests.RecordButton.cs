using Flyback.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.Editor.Assist;
using Flyback.Editor.Canvas;
using Flyback.Editor.Capture;
using Flyback.Editor.Controls;
using Flyback.Engine.Graph;
using Flyback.Ui.Controls;
using Flyback.Editor.Knobs;
using Flyback.Editor.Notices;
using Flyback.Editor.Settings;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;
using Flyback.Ui;
using Flyback.Host;

namespace Flyback.Editor.Tests.Settings;

public partial class OutputSettingsTests
{
    // --- the record button ---------------------------------------------------

    /// <summary>
    /// Named rather than found by content: it lives on the toolbar now and a
    /// glyph, unlike the label it replaced, has nothing a test can read.
    /// </summary>
    private static Button Record(MainWindow window) => Named<Button>(window, "record");

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
