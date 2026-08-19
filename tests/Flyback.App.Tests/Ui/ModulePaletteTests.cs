using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The module list, and the gesture that opens it.
/// </summary>
/// <remarks>
/// It used to be a column down the left of the window, standing open whether or
/// not anything was being added. Now it appears at the pointer when the canvas
/// is right-clicked, and what is picked lands where the click was — so what is
/// checked here is that gesture, and that the right button has not lost the
/// panning it also does.
/// </remarks>
public class ModulePaletteTests : UiTest
{
    private static MainWindow Open()
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    /// <summary>The palette, wherever it currently is — it is not in the window's own tree once shown in a flyout.</summary>
    private static ModulePalette? Palette(MainWindow window) =>
        All<ModulePalette>(window).FirstOrDefault();

    /// <summary>
    /// A corner of the canvas that is on screen and has nothing on it.
    /// </summary>
    /// <remarks>
    /// In the editor's own coordinates rather than the patch's: a point in graph
    /// space may be anywhere, including well outside the window, and a press
    /// there never reaches the control at all — which looks exactly like the
    /// gesture not working.
    /// </remarks>
    private static Point Empty(MainWindow window)
    {
        var editor = Editor(window);

        return editor.TranslatePoint(new Point(24, editor.Bounds.Height - 24), window)
            ?? throw new InvalidOperationException("the editor is not in this window");
    }

    /// <summary>The same point said in graph space, which is where a module put there would land.</summary>
    private static Point InGraph(MainWindow window, Point onWindow)
    {
        var editor = Editor(window);
        var onEditor = window.TranslatePoint(onWindow, editor) ?? onWindow;

        return editor.GraphToScreen.Invert().Transform(onEditor);
    }

    private static void RightClick(MainWindow window, Point onWindow)
    {
        window.MouseDown(onWindow, MouseButton.Right);
        window.MouseUp(onWindow, MouseButton.Right);
        Settle(window);
    }

    // --- the panel is gone --------------------------------------------------

    /// <summary>
    /// The window opens with the canvas and the inspector, and no third column
    /// standing open for a list nobody has asked for.
    /// </summary>
    [AvaloniaFact]
    public void The_shell_no_longer_keeps_a_column_for_the_module_list()
    {
        var window = Open();
        var columns = All<Grid>(window).First(g => g.Name == "columns");

        columns.ColumnDefinitions.Count.ShouldBe(3, "canvas, splitter, inspector");
        All<ModulePalette>(window).ShouldBeEmpty("nothing is showing it yet");
    }

    // --- the gesture --------------------------------------------------------

    /// <summary>
    /// And it is a list with something in it. Measured as well as found, because
    /// a popup that opens with no size is exactly as useless as one that does not
    /// open, and looks the same to everything except a person.
    /// </summary>
    [AvaloniaFact]
    public void Right_clicking_the_canvas_opens_the_module_list()
    {
        var window = Open();

        RightClick(window, Empty(window));

        var palette = Palette(window).ShouldNotBeNull("the palette should be up");

        palette.Bounds.Width.ShouldBeGreaterThan(100);
        palette.Bounds.Height.ShouldBeGreaterThan(100);
        All<Button>(palette).Count().ShouldBeGreaterThan(10, "the catalogue should be in it");
    }

    /// <summary>
    /// A right-click on a module is about that module, not about adding another
    /// one beside it.
    /// </summary>
    [AvaloniaFact]
    public void Right_clicking_a_module_opens_nothing()
    {
        var window = Open();
        var editor = Editor(window);
        var node = editor.Patch.Nodes[0];

        var body = editor.GraphToScreen.Transform(new Point(
            node.X + NodeGeometry.Width / 2,
            node.Y + NodeGeometry.HeaderHeight / 2));

        RightClick(window, editor.TranslatePoint(body, window)!.Value);

        Palette(window).ShouldBeNull();
    }

    /// <summary>
    /// The right button no longer pans — that is the middle one alone. It opens
    /// on the press, which it can now that nothing has to be told apart from it.
    /// </summary>
    [AvaloniaFact]
    public void The_right_button_opens_the_list_and_does_not_pan()
    {
        var window = Open();
        var editor = Editor(window);

        var before = editor.GraphToScreen.Transform(new Point(0, 0));

        var from = Empty(window);
        var to = from + new Vector(120, 90);

        window.MouseDown(from, MouseButton.Right);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Right);
        Settle(window);

        editor.GraphToScreen.Transform(new Point(0, 0)).ShouldBe(before, "the view should not have moved");
    }

    // --- how it looks --------------------------------------------------------

    /// <summary>
    /// The presenter holding the list gives up its padding, its border and its
    /// background, and the list keeps them. Worth pinning because the way this
    /// fails is silent: a selector that matches nothing leaves the presenter
    /// dressed as a menu and there is no error anywhere.
    /// </summary>
    [AvaloniaFact]
    public void The_flyout_presenter_gives_its_frame_up_to_the_list()
    {
        var window = Open();

        RightClick(window, Empty(window));

        var palette = Palette(window).ShouldNotBeNull();
        var presenter = All<FlyoutPresenter>(window)
            .First(p => p.Classes.Contains(ModulePalette.PresenterClass));

        presenter.Padding.ShouldBe(new Thickness(0));
        presenter.BorderThickness.ShouldBe(new Thickness(0));

        // The list is the one thing painting a background, so the translucency
        // has nothing opaque sitting behind it.
        palette.Opacity.ShouldBe(ModulePalette.Translucency);
        palette.Content.ShouldBeOfType<Border>().Background.ShouldNotBeNull();
    }

    // --- the keyboard --------------------------------------------------------

    /// <summary>
    /// Space opens it too, at the pointer — so the hand never has to leave the
    /// keyboard, and what arrives still lands where the eye is.
    /// </summary>
    [AvaloniaFact]
    public void Space_opens_the_list_where_the_pointer_last_was()
    {
        var window = Open();
        var editor = Editor(window);

        var onWindow = Empty(window);
        var spot = InGraph(window, onWindow);

        // Somewhere for the pointer to have been, without pressing anything.
        window.MouseMove(onWindow);
        Settle(window);

        editor.Focus();
        PressKey(editor, Key.Space);
        Settle(window);

        var palette = Palette(window).ShouldNotBeNull();
        Press(All<Button>(palette).First(b => (b.Content as string) == "Sine"));
        Settle(window);

        var added = editor.Patch.Nodes.Last(n => n.TypeId == "osc.sine");
        var def = NodeCatalog.BuiltIn.Require("osc.sine");

        (added.X + NodeGeometry.Width / 2).ShouldBe(spot.X, 1);
        (added.Y + NodeGeometry.Height(def) / 2).ShouldBe(spot.Y, 1);
    }

    /// <summary>
    /// Typing narrows and Enter takes the first match, which is the whole gesture
    /// without an arrow key in it.
    /// </summary>
    [AvaloniaFact]
    public void Typing_then_enter_adds_the_first_match()
    {
        var window = Open();
        var editor = Editor(window);

        RightClick(window, Empty(window));

        var palette = Palette(window).ShouldNotBeNull();
        var box = All<TextBox>(palette).First();

        box.Text = "kaleido";
        Settle(window);

        PressKey(box, Key.Enter);
        Settle(window);

        editor.SelectedNode.ShouldNotBeNull().TypeId.ShouldBe("space.kaleidoscope");
    }

    /// <summary>
    /// The arrows walk the modules and nothing else — a list with headings in it
    /// would otherwise step onto one of those and Enter would have nothing to add.
    /// </summary>
    [AvaloniaFact]
    public void The_arrows_walk_the_list_and_enter_adds_where_they_stopped()
    {
        var window = Open();
        var editor = Editor(window);

        RightClick(window, Empty(window));

        var palette = Palette(window).ShouldNotBeNull();
        var box = All<TextBox>(palette).First();

        box.Text = "osc.";
        Settle(window);

        var names = All<Button>(palette)
            .Where(b => b.Content is string)
            .Select(b => (string)b.Content!)
            .ToArray();

        PressKey(box, Key.Down);
        PressKey(box, Key.Down);
        PressKey(box, Key.Up);
        PressKey(box, Key.Enter);
        Settle(window);

        // Down twice and up once is the second of them.
        var wanted = NodeCatalog.BuiltIn.All.First(d => d.Name == names[1]).TypeId;

        editor.SelectedNode.ShouldNotBeNull().TypeId.ShouldBe(wanted);
    }

    /// <summary>
    /// Up at the top of the list stays at the top rather than wrapping round to
    /// the bottom, which would lose the reader's place.
    /// </summary>
    [AvaloniaFact]
    public void The_highlight_stops_at_the_ends_rather_than_wrapping()
    {
        var window = Open();
        var editor = Editor(window);

        RightClick(window, Empty(window));

        var palette = Palette(window).ShouldNotBeNull();
        var box = All<TextBox>(palette).First();

        box.Text = "osc.";
        Settle(window);

        var first = All<Button>(palette).First(b => b.Content is string).Content as string;

        for (var i = 0; i < 5; i++) PressKey(box, Key.Up);
        PressKey(box, Key.Enter);
        Settle(window);

        var added = editor.SelectedNode.ShouldNotBeNull();

        NodeCatalog.BuiltIn.Require(added.TypeId).Name.ShouldBe(first);
    }

    /// <summary>
    /// Space is a character and the box it belongs in is right there, so it must
    /// not also be the key that confirms.
    /// </summary>
    [AvaloniaFact]
    public void Space_in_the_filter_is_typing_rather_than_confirming()
    {
        var window = Open();
        var editor = Editor(window);

        var before = editor.Patch.Nodes.Count;

        RightClick(window, Empty(window));

        var palette = Palette(window).ShouldNotBeNull();
        PressKey(All<TextBox>(palette).First(), Key.Space);
        Settle(window);

        editor.Patch.Nodes.Count.ShouldBe(before, "nothing should have been added");
        Palette(window).ShouldNotBeNull("and the list should still be up");
    }

    // --- what comes out of it -----------------------------------------------

    /// <summary>
    /// The whole point of the gesture: the module lands where the canvas was
    /// clicked rather than in the middle of the view.
    /// </summary>
    [AvaloniaFact]
    public void A_module_picked_from_it_lands_where_the_canvas_was_clicked()
    {
        var window = Open();
        var editor = Editor(window);

        var onWindow = Empty(window);
        var spot = InGraph(window, onWindow);

        RightClick(window, onWindow);

        var palette = Palette(window).ShouldNotBeNull();
        var button = All<Button>(palette).First(b => (b.Content as string) == "Sine");

        Press(button);
        Settle(window);

        var added = editor.Patch.Nodes.LastOrDefault(n => n.TypeId == "osc.sine").ShouldNotBeNull();
        var def = NodeCatalog.BuiltIn.Require("osc.sine");

        // Centred on the click, which is what "lands here" means for a block
        // that has a width and a height.
        (added.X + NodeGeometry.Width / 2).ShouldBe(spot.X, 1);
        (added.Y + NodeGeometry.Height(def) / 2).ShouldBe(spot.Y, 1);
    }

    [AvaloniaFact]
    public void Picking_a_module_selects_it_and_closes_the_list()
    {
        var window = Open();
        var editor = Editor(window);

        RightClick(window, Empty(window));

        var palette = Palette(window).ShouldNotBeNull();
        Press(All<Button>(palette).First(b => (b.Content as string) == "Sine"));
        Settle(window);

        editor.SelectedNode.ShouldNotBeNull().TypeId.ShouldBe("osc.sine");
        Palette(window).ShouldBeNull("the list closes behind what was picked");
    }

    /// <summary>
    /// The Output is never listed. Every patch has one and cannot have two, so a
    /// button for it could only ever pick the one already there.
    /// </summary>
    [AvaloniaFact]
    public void The_output_is_not_in_the_list()
    {
        var window = Open();

        RightClick(window, Empty(window));

        var palette = Palette(window).ShouldNotBeNull();

        All<Button>(palette).ShouldNotContain(b => (b.Content as string) == "Output");
    }

    /// <summary>
    /// Typing narrows it, which is the fast way to a module and the reason the
    /// filter is the first thing in the list.
    /// </summary>
    [AvaloniaFact]
    public void Typing_in_the_filter_narrows_the_list()
    {
        var window = Open();

        RightClick(window, Empty(window));

        var palette = Palette(window).ShouldNotBeNull();
        var box = All<TextBox>(palette).First();

        var everything = All<Button>(palette).Count();

        box.Text = "sine";
        Settle(window);

        var narrowed = All<Button>(palette).Count();

        narrowed.ShouldBeLessThan(everything);
        narrowed.ShouldBeGreaterThan(0);
    }

    private static void Press(Button button)
    {
        button.Focus();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void PressKey(InputElement target, Key key) =>
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            Source = target,
        });
}
