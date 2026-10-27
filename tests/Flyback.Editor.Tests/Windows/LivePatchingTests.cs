using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.Editor.Composition;
using Flyback.Editor.Hubs;
using Flyback.Editor.Windows;
using Flyback.Ui.Controls;
using Shouldly;

namespace Flyback.Editor.Tests.Windows;

/// <summary>
/// The modules over a full-screen picture: Shift+F11 or the View menu goes there, Tab
/// over a full-screen picture puts them on or takes them off, and Escape gives the
/// window back with the canvas where it was.
/// </summary>
public class LivePatchingTests : EditorTest
{
    private static PreviewHost Preview(MainWindow window) => All<PreviewHost>(window).Single();

    private static Grid Columns(MainWindow window) => All<Grid>(window).First(g => g.Name == "columns");

    private static void Press(MainWindow window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
        Settle(window);
    }

    private static void GoLive(MainWindow window) => Press(window, Key.F11, RawInputModifiers.Shift, PhysicalKey.F11);

    private static void PressTab(MainWindow window) => Press(window, Key.Tab, RawInputModifiers.None, PhysicalKey.Tab);

    private static void PressEscape(MainWindow window) => Press(window, Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);

    [AvaloniaFact]
    public void Shift_F11_puts_the_modules_over_the_full_screen_picture()
    {
        var window = Open();
        var editor = Editor(window);

        GoLive(window);

        Preview(window).IsEffectivelyVisible.ShouldBeTrue("the picture has the window");
        editor.IsEffectivelyVisible.ShouldBeTrue("and the modules stand over it");
        editor.SeeThrough.ShouldBeTrue("drawn without the canvas's ground, so the picture shows");
        editor.GetVisualParent().ShouldBeSameAs(Columns(window), "lifted into the picture's cell");
        Grid.GetColumn(editor).ShouldBe(Grid.GetColumn(All<Border>(window).First(b => b.Child is PreviewHost)));
        editor.Bounds.Width.ShouldBe(Preview(window).Bounds.Width, 1, "as wide as the picture");
        Columns(window).ColumnDefinitions.Count(c => c.ActualWidth > 1).ShouldBe(1, "the rest of the shell is put away");
    }

    /// <summary>The picture is a GPU surface that rebuilds when it changes parents, so the canvas moves instead.</summary>
    [AvaloniaFact]
    public void The_picture_is_never_moved_to_get_there()
    {
        var window = Open();
        var preview = Preview(window);
        var parent = preview.GetVisualParent();

        GoLive(window);

        preview.GetVisualParent().ShouldBeSameAs(parent);

        PressEscape(window);

        preview.GetVisualParent().ShouldBeSameAs(parent);
    }

    [AvaloniaFact]
    public void Tab_over_a_full_screen_picture_puts_the_modules_on_and_takes_them_off()
    {
        var window = Open();
        var editor = Editor(window);

        DoubleClickPicture(window);
        editor.IsEffectivelyVisible.ShouldBeFalse("full screen starts with the picture alone");

        PressTab(window);
        editor.IsEffectivelyVisible.ShouldBeTrue();
        editor.SeeThrough.ShouldBeTrue();

        PressTab(window);
        editor.IsEffectivelyVisible.ShouldBeFalse("Tab again gives the picture back to itself");
        Preview(window).IsEffectivelyVisible.ShouldBeTrue("still full screen");
        editor.SeeThrough.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void F11_gives_the_picture_the_window_and_takes_it_back()
    {
        var window = Open();

        Press(window, Key.F11, RawInputModifiers.None, PhysicalKey.F11);
        Service<FullScreenPreview>(window).IsFullScreen.ShouldBeTrue();
        Editor(window).IsEffectivelyVisible.ShouldBeFalse("the picture alone, until Tab");

        Press(window, Key.F11, RawInputModifiers.None, PhysicalKey.F11);
        Service<FullScreenPreview>(window).IsFullScreen.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Tab_moves_the_focus_as_ever_while_the_shell_is_showing()
    {
        var window = Open();
        var editor = Editor(window);
        var pane = editor.GetVisualParent();

        PressTab(window);

        Service<LivePatching>(window).IsOn.ShouldBeFalse();
        editor.GetVisualParent().ShouldBeSameAs(pane);
        editor.SeeThrough.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Escape_puts_the_canvas_back_where_it_was()
    {
        var window = Open();
        var editor = Editor(window);
        var pane = editor.GetVisualParent();
        var row = Grid.GetRow(editor);
        var widths = Columns(window).ColumnDefinitions.Select(c => c.ActualWidth).ToList();

        GoLive(window);
        PressEscape(window);

        editor.GetVisualParent().ShouldBeSameAs(pane);
        Grid.GetRow(editor).ShouldBe(row);
        editor.IsEffectivelyVisible.ShouldBeTrue();
        editor.SeeThrough.ShouldBeFalse();
        editor.Opacity.ShouldBe(1);
        Service<LivePatching>(window).IsOn.ShouldBeFalse();

        foreach (var (was, now) in widths.Zip(Columns(window).ColumnDefinitions.Select(c => c.ActualWidth)))
            now.ShouldBe(was, 0.5, "every column comes back to the width it had");
    }

    [AvaloniaFact]
    public void Shift_F11_again_gives_the_window_back()
    {
        var window = Open();
        var editor = Editor(window);
        var pane = editor.GetVisualParent();

        GoLive(window);
        GoLive(window);

        Service<FullScreenPreview>(window).IsFullScreen.ShouldBeFalse();
        editor.GetVisualParent().ShouldBeSameAs(pane);
    }

    private static SourceView Source(MainWindow window) => All<SourceView>(window).Single();

    private static void PressF2(MainWindow window) => Press(window, Key.F2, RawInputModifiers.None, PhysicalKey.F2);

    private void TextOwns(MainWindow window)
    {
        var document = Service<Document>(window);
        document.TakeSource(document.AsText());
        Settle(window);
    }

    [AvaloniaFact]
    public void A_patch_its_text_owns_goes_live_as_text()
    {
        var window = Open();
        var source = Source(window);
        var pane = source.GetVisualParent();

        TextOwns(window);
        GoLive(window);

        source.IsEffectivelyVisible.ShouldBeTrue("the text stands over the picture");
        source.SeeThrough.ShouldBeTrue("with no ground, so the picture shows between the lines");
        source.GetVisualParent().ShouldBeSameAs(Columns(window));
        Editor(window).IsEffectivelyVisible.ShouldBeFalse("the modules stay put away");
        Service<LivePatching>(window).Coding.ShouldBeTrue();

        PressEscape(window);

        source.GetVisualParent().ShouldBeSameAs(pane);
        source.SeeThrough.ShouldBeFalse();
        source.IsVisible.ShouldBeTrue("and the text is back in front of the canvas");
        Editor(window).IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_patch_the_modules_own_goes_live_as_modules_even_from_the_text_view()
    {
        var window = Open();
        var live = Service<LivePatching>(window);

        PressF2(window);
        GoLive(window);

        live.Coding.ShouldBeFalse("only the view that owns the patch goes over the picture");
        Editor(window).GetVisualParent().ShouldBeSameAs(Columns(window));
        Source(window).IsEffectivelyVisible.ShouldBeFalse();

        PressF2(window);

        live.Coding.ShouldBeFalse("F2 does not swap the view over the picture");
        Editor(window).IsEffectivelyVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void F2_over_the_text_live_leaves_the_text_there()
    {
        var window = Open();
        var live = Service<LivePatching>(window);

        TextOwns(window);
        GoLive(window);
        PressF2(window);

        live.Coding.ShouldBeTrue();
        Source(window).IsEffectivelyVisible.ShouldBeTrue();
    }

    /// <summary>Over the text Tab indents, so it is Escape or Shift+F11 that leaves.</summary>
    [AvaloniaFact]
    public void Tab_over_the_text_is_the_text_s_own()
    {
        var window = Open();

        TextOwns(window);
        GoLive(window);
        PressTab(window);

        Service<LivePatching>(window).IsOn.ShouldBeTrue();
        Source(window).IsEffectivelyVisible.ShouldBeTrue();
    }

    /// <summary>The text area takes Escape for itself, so leaving has to be answered before it gets there.</summary>
    [AvaloniaFact]
    public void Escape_with_the_caret_in_the_text_leaves_the_picture()
    {
        var window = Open();

        TextOwns(window);
        GoLive(window);
        All<AvaloniaEdit.Editing.TextArea>(window).Single().Focus();
        PressEscape(window);

        Service<LivePatching>(window).IsOn.ShouldBeFalse();
        Service<FullScreenPreview>(window).IsFullScreen.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Over_the_picture_the_text_has_no_row_of_buttons_and_no_band_across_the_caret_s_line()
    {
        var window = Open();

        TextOwns(window);
        var editor = All<AvaloniaEdit.TextEditor>(window).Single();
        var apply = All<Button>(window).Single(button => button.Name == "apply");
        GoLive(window);

        apply.IsEffectivelyVisible.ShouldBeFalse("Ctrl+Enter applies, and the row would hide the picture");
        editor.Options.HighlightCurrentLine.ShouldBeFalse("the caret's line is banded behind its text like the rest");

        PressEscape(window);

        apply.IsEffectivelyVisible.ShouldBeTrue();
        editor.Options.HighlightCurrentLine.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Space_over_the_text_does_not_pause()
    {
        var window = Open();
        Play(window);

        TextOwns(window);
        GoLive(window);
        Press(window, Key.Space, RawInputModifiers.None, PhysicalKey.Space);

        Service<Playback>(window).Paused.ShouldBeFalse("a space typed into the text is a space");
    }

    [AvaloniaFact]
    public void The_modules_fade_when_left_alone_and_come_back_at_a_move()
    {
        var window = Open();
        var editor = Editor(window);
        var fade = Service<LivePatching>(window).Fade;

        GoLive(window);
        fade.IsFaded.ShouldBeFalse("shown to begin with");

        fade.Fade();
        fade.IsFaded.ShouldBeTrue();

        window.MouseMove(new Point(editor.Bounds.Width / 2, editor.Bounds.Height / 2));
        Settle(window);

        fade.IsFaded.ShouldBeFalse("a move brings them back");
    }

    [AvaloniaFact]
    public void Going_live_from_the_shell_gives_the_picture_the_window_with_the_modules_over_it()
    {
        var window = Open();

        Service<LivePatching>(window).Toggle();
        Settle(window);

        Service<LivePatching>(window).IsOn.ShouldBeTrue();
        Editor(window).IsEffectivelyVisible.ShouldBeTrue();
        Service<FullScreenPreview>(window).IsFullScreen.ShouldBeTrue();
    }
}
