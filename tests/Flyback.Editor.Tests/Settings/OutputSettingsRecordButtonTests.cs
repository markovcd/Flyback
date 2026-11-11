using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Flyback.Editor.Composition;
using Flyback.Editor.Notices;
using Shouldly;
using Colors = Flyback.Ui.Controls.Colors;
using Path = Avalonia.Controls.Shapes.Path;

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
        var paths = Record(window).GetLogicalDescendants().OfType<Path>().ToList();

        paths.ShouldContain(
            path => path.Data != null && path.Fill != null,
            "the dot is filled, in the button's own foreground");
    }

    /// <summary>While a take runs the square is drawn in the fault red, the one red every scheme shares; the count-in and the button at rest keep the button's own ink.</summary>
    [AvaloniaFact]
    public void The_record_button_turns_red_while_a_take_runs()
    {
        var window = Open();
        var reactions = Service<Reactions>(window);

        reactions.Raise(new TakeMarked(TakeMode.Counting));
        RedFills(window).ShouldBe(0, "a count-in has not started the take");

        reactions.Raise(new TakeMarked(TakeMode.Recording));
        RedFills(window).ShouldBeGreaterThan(0, "a running take draws its square in red");

        reactions.Raise(new TakeMarked(TakeMode.Ready));
        RedFills(window).ShouldBe(0, "a finished take gives the dot back its own ink");
    }

    private static int RedFills(MainWindow window) =>
        Record(window).GetLogicalDescendants().OfType<Path>()
            .Count(path => path.Fill is ISolidColorBrush brush && brush.Color == Colors.Fault);
}
