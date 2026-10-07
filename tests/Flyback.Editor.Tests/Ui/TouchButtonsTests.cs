using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Flyback.Core.Graph;
using Flyback.Editor.Controls;
using Flyback.Editor.Windows;
using Shouldly;

namespace Flyback.Editor.Tests.Ui;

/// <summary>
/// The buttons for what a keyboard does and a finger cannot: the clipboard, select-all,
/// laying out the selection alone and the code view's text size.
/// </summary>
public class TouchButtonsTests : EditorTest
{
    private static Patch Trio(out NodeInstance clock, out NodeInstance sine, out NodeInstance output)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        clock = builder.Add("time", 500, 300);
        sine = builder.Add("osc.sine", 100, 100);
        output = builder.Add(NodeCatalog.OutputTypeId, 900, 100);
        builder.Wire(clock, 0, sine, 0);

        return builder.Patch;
    }

    private static Button Named(Window window, string name) => All<Button>(window).Single(b => b.Name == name);

    private static void Select(MainWindow window, params NodeInstance[] nodes)
    {
        var editor = Editor(window);

        editor.Selection.Take([.. nodes.Select(n => n.Id)]);
        editor.Selection.Announce();
        Settle(window);
    }

    private static void Touch(MainWindow window)
    {
        var editor = Editor(window);
        var finger = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);

        editor.Fingers.Down(editor, finger, new Point(20, 20), 1_000);
        editor.Fingers.Up(editor, finger, new Point(20, 20), 1_050);
        Settle(window);
    }

    [AvaloniaFact]
    public void The_toolbar_shows_select_all_and_paste_once_a_finger_touches_the_canvas()
    {
        var window = Open();

        var selectAll = Named(window, "select-all");
        var paste = Named(window, "paste");

        selectAll.IsVisible.ShouldBeFalse();
        paste.IsVisible.ShouldBeFalse();

        Touch(window);

        selectAll.IsVisible.ShouldBeTrue();
        paste.IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Select_all_picks_out_every_module()
    {
        var window = Open(Trio(out _, out _, out _));

        Press(Named(window, "select-all"));
        Settle(window);

        Editor(window).Selection.Count.ShouldBe(3);
    }

    [AvaloniaFact]
    public void The_inspector_copies_the_selection_to_the_clipboard()
    {
        var window = Open(Trio(out var clock, out _, out _));

        Select(window, clock);
        Press(Named(window, "copy-modules"));

        var clipboard = TopLevel.GetTopLevel(window)!.Clipboard!;

        Pump(() => clipboard.TryGetTextAsync().GetAwaiter().GetResult() is { Length: > 0 }, window);

        clipboard.TryGetTextAsync().GetAwaiter().GetResult().ShouldNotBeNull().ShouldContain("\"time\"");
    }

    [AvaloniaFact]
    public void The_inspector_cuts_the_selection_off_the_canvas()
    {
        var window = Open(Trio(out var clock, out _, out _));
        var editor = Editor(window);

        Select(window, clock);
        Press(Named(window, "cut-modules"));

        Pump(() => editor.History.Patch.Nodes.All(n => n.TypeId != "time"), window);
        editor.History.CanUndo.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Paste_adds_what_the_clipboard_holds()
    {
        var window = Open(Trio(out var clock, out _, out _));
        var editor = Editor(window);

        Select(window, clock);
        Press(Named(window, "copy-modules"));

        var clipboard = TopLevel.GetTopLevel(window)!.Clipboard!;

        Pump(() => clipboard.TryGetTextAsync().GetAwaiter().GetResult() is { Length: > 0 }, window);

        Press(Named(window, "paste"));

        Pump(() => editor.History.Patch.Nodes.Count(n => n.TypeId == "time") == 2, window);
    }

    [AvaloniaFact]
    public void The_inspector_lays_out_only_the_selected_modules()
    {
        var window = Open(Trio(out var clock, out var sine, out var output));

        Select(window, clock, sine);
        Press(Named(window, "tidy-selection"));
        Settle(window);

        clock.X.ShouldBeLessThan(sine.X, "a wire runs left to right once laid out");
        (output.X, output.Y).ShouldBe((900d, 100d), "what was not selected stays where it was");
    }

    [AvaloniaFact]
    public void A_lone_module_has_no_button_to_lay_out_the_selection()
    {
        var window = Open(Trio(out var clock, out _, out _));

        Select(window, clock);

        All<Button>(window).ShouldNotContain(b => b.Name == "tidy-selection");
    }

    [AvaloniaFact]
    public void The_code_view_steps_its_text_size_with_buttons()
    {
        var window = Open();

        All<Avalonia.Controls.Primitives.ToggleButton>(window).Single(b => b.Name == "code").IsChecked = true;
        Settle(window);

        var source = All<SourceView>(window).Single();
        var size = source.EditorFontSize;

        Press(Named(window, "text-larger"));
        source.EditorFontSize.ShouldBe(size + 1);

        Press(Named(window, "text-smaller"));
        Press(Named(window, "text-smaller"));
        source.EditorFontSize.ShouldBe(size - 1);
    }
}
