using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Double-clicking the name at the top of the inspector turns it into a box, and
/// what is typed there is what the module is called — on the panel and on the
/// canvas, which draws its headers from the same name.
/// </summary>
/// <remarks>
/// The canvas is checked through the patch rather than through the pixels: the
/// header is drawn straight from <see cref="NodeInstance.Title"/>, so what is
/// worth holding is that the name reached the module and that the canvas was
/// asked to draw again. The alternative is reading text back out of a rendered
/// frame, which would be a test of the font.
/// </remarks>
public class RenameModuleTests : UiTest
{
    private static MainWindow Open(out NodeInstance sine)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var osc = b.Add("osc.sine", 360, 40);
        var screen = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        b.Wire(osc, 0, screen, NodeCatalog.OutputColorPort);

        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Editor(window).Patch = b.Patch;
        Settle(window);

        // The patch on the canvas is the one just handed over, so the module in
        // it is the same object — nothing here reopens or round-trips it.
        sine = Editor(window).Patch.Find(osc.Id)
            ?? throw new InvalidOperationException("the oscillator did not survive being opened");

        Select(window, sine);
        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static void Select(MainWindow window, NodeInstance node)
    {
        var editor = Editor(window);

        var body = new Point(
            node.X + NodeGeometry.Width / 2,
            node.Y + NodeGeometry.HeaderHeight / 2);

        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(body), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    /// <summary>The heading on the panel — the one control there at that size.</summary>
    private static TextBlock Title(MainWindow window) =>
        All<TextBlock>(window).First(t => t.FontSize == 17);

    private static TextBox? Box(MainWindow window) =>
        All<TextBox>(window).FirstOrDefault(t => t.FontSize == 17);

    private static void DoubleClickTitle(MainWindow window)
    {
        var title = Title(window);

        var at = title.TranslatePoint(new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("the title is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    private static void Type(MainWindow window, string text)
    {
        var box = Box(window).ShouldNotBeNull("the title should have become a box");

        box.Text = text;
        Settle(window);
    }

    private static void Press(MainWindow window, Key key)
    {
        Box(window).ShouldNotBeNull().RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
        });

        Settle(window);
    }

    [AvaloniaFact]
    public void The_panel_names_the_module_by_its_definition_to_begin_with()
    {
        var window = Open(out _);

        Title(window).Text.ShouldBe("Sine");
        Box(window).ShouldBeNull();
    }

    [AvaloniaFact]
    public void Double_clicking_the_name_turns_it_into_a_box()
    {
        var window = Open(out _);

        DoubleClickTitle(window);

        // Empty rather than filled in with "Sine": the box holds the name the
        // module has, and it has none. The definition's name is the watermark.
        var box = Box(window).ShouldNotBeNull("the title should have become a box");
        box.Text.ShouldBeNullOrEmpty();
        box.PlaceholderText.ShouldBe("Sine");
    }

    [AvaloniaFact]
    public void What_is_typed_becomes_the_name_on_the_panel_and_in_the_patch()
    {
        var window = Open(out var sine);

        DoubleClickTitle(window);
        Type(window, "Wobble");
        Press(window, Key.Enter);

        sine.Name.ShouldBe("Wobble");
        sine.Title(NodeCatalog.BuiltIn.Require("osc.sine")).ShouldBe("Wobble");

        // Back to a label, and the label is the new name.
        Box(window).ShouldBeNull();
        Title(window).Text.ShouldBe("Wobble");
    }

    /// <summary>
    /// The whole of what "it should also change in the graph" needs: the canvas
    /// draws the header from the module's title, and a rename is an edit, so it
    /// goes through the history and the canvas is asked to draw again.
    /// </summary>
    [AvaloniaFact]
    public void A_rename_is_an_edit_that_can_be_taken_back()
    {
        var window = Open(out var sine);
        var editor = Editor(window);

        DoubleClickTitle(window);
        Type(window, "Wobble");
        Press(window, Key.Enter);

        editor.CanUndo.ShouldBeTrue("renaming is an edit like any other");
        editor.IsModified.ShouldBeTrue();

        editor.Undo().ShouldBeTrue();

        // Undo restores a patch read back from the history, so the module is a
        // fresh object with the same id rather than the one renamed above.
        editor.Patch.Find(sine.Id).ShouldNotBeNull().Name.ShouldBeNull();
        Title(window).Text.ShouldBe("Sine");
    }

    [AvaloniaFact]
    public void Emptying_the_box_puts_the_definitions_name_back()
    {
        var window = Open(out var sine);

        DoubleClickTitle(window);
        Type(window, "Wobble");
        Press(window, Key.Enter);

        DoubleClickTitle(window);
        Type(window, string.Empty);
        Press(window, Key.Enter);

        sine.Name.ShouldBeNull();
        Title(window).Text.ShouldBe("Sine");
    }

    [AvaloniaFact]
    public void Escape_abandons_what_was_typed()
    {
        var window = Open(out var sine);

        DoubleClickTitle(window);
        Type(window, "Wobble");
        Press(window, Key.Escape);

        sine.Name.ShouldBeNull();
        Box(window).ShouldBeNull("the box should have closed");
        Title(window).Text.ShouldBe("Sine");
    }

    /// <summary>
    /// Opening the box and closing it again without typing is not an edit. A
    /// press of undo that puts nothing back is worse than no undo at all.
    /// </summary>
    [AvaloniaFact]
    public void Closing_the_box_unchanged_is_not_an_edit()
    {
        var window = Open(out _);

        DoubleClickTitle(window);
        Press(window, Key.Enter);

        Editor(window).CanUndo.ShouldBeFalse();
        Editor(window).IsModified.ShouldBeFalse();
    }

    /// <summary>
    /// Selecting something else while the box is open keeps what was typed. It
    /// is the same commit as Enter — leaving a field is how a field ends
    /// everywhere else, and a rename lost to a stray click would be the kind of
    /// thing nobody notices until the name is gone.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_away_keeps_what_was_typed()
    {
        var window = Open(out var sine);
        var screen = Editor(window).Patch.Output;

        DoubleClickTitle(window);
        Type(window, "Wobble");

        Select(window, screen);

        sine.Name.ShouldBe("Wobble");
    }
}
