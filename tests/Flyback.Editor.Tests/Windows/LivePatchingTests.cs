using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.Editor.Composition;
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

    /// <summary>The text view keeps its place: it comes back showing if it was showing.</summary>
    [AvaloniaFact]
    public void A_patch_showing_as_text_comes_back_as_text()
    {
        var window = Open();
        var editor = Editor(window);

        window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        Settle(window);
        editor.IsVisible.ShouldBeFalse("the text stands in the canvas's place");

        GoLive(window);
        editor.IsEffectivelyVisible.ShouldBeTrue("the modules are what stands over the picture");

        PressEscape(window);
        editor.IsVisible.ShouldBeFalse("and the text is back in front");
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
