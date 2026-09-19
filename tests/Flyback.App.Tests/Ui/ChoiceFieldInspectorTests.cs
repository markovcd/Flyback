using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// A field that is one of a list is a list on the panel, and every pick from it
/// is kept — Ink's mode is the one the engine ships.
/// </summary>
public class ChoiceFieldInspectorTests : UiTest
{
    private static MainWindow Open(out NodeInstance ink)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var placed = b.Add("color.ink", 360, 40);
        var screen = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        b.Wire(placed, 0, screen, NodeCatalog.OutputColorPort);

        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var editor = All<NodeEditor>(window).Single();

        editor.Patch = b.Patch;
        Settle(window);

        ink = editor.Patch.Find(placed.Id)
            ?? throw new InvalidOperationException("the ink did not survive being opened");

        var body = new Point(ink.X + NodeGeometry.Width / 2, ink.Y + NodeGeometry.HeaderHeight / 2);

        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(body), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        return window;
    }

    private static Picker Mode(MainWindow window) => All<Picker>(window)
        .Single(list => list.ItemsSource?.Cast<object>().OfType<ChoiceOption>().Any(o => o.Name == "Over") == true);

    private static string Held(NodeInstance ink) =>
        ink.StateOf(NodeCatalog.InkStateKey)?[NodeCatalog.InkModeKey]?.GetValue<string>() ?? string.Empty;

    /// <summary>
    /// Back to where the list started as well as away from it: the row compares a
    /// pick with what is held, so what is held has to move with each pick or the
    /// way back reads as no change.
    /// </summary>
    [AvaloniaFact]
    public void Every_pick_is_kept_including_the_way_back()
    {
        var window = Open(out var ink);

        Mode(window).SelectedIndex = 1;
        Settle(window);
        Held(ink).ShouldBe(NodeCatalog.InkOver);

        Mode(window).SelectedIndex = 0;
        Settle(window);
        Held(ink).ShouldBe("add");

        Mode(window).SelectedIndex = 1;
        Settle(window);
        Held(ink).ShouldBe(NodeCatalog.InkOver);
    }
}
