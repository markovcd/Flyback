using Avalonia.Controls;
using Avalonia.Threading;
using Flyback.Editor.Canvas;
using Flyback.Editor.Statistics;

namespace Flyback.Editor.Windows;

/// <summary>
/// The modules drawn over a full-screen picture and edited there, for playing a patch live
/// in front of people.
/// </summary>
/// <remarks>
/// The canvas is lifted out of its pane into the preview's cell, just above the picture,
/// rather than the picture being moved: the preview is a GPU surface that rebuilds when
/// reparented, and the canvas is drawn and moves for free. The transport and the knobs
/// over the picture stay above it.
/// </remarks>
internal sealed class LivePatching
{
    private readonly FullScreenPreview fullScreen;
    private readonly NodeEditor editor;
    private readonly Usage usage;
    private readonly ModulesFade fade;

    private Panel? home;
    private int homeIndex;
    private int homeRow;
    private bool shownBefore;

    public LivePatching(FullScreenPreview fullScreen, NodeEditor editor, Usage usage)
    {
        this.fullScreen = fullScreen;
        this.editor = editor;
        this.usage = usage;
        fade = new ModulesFade(() => editor.Gestures.Gesturing);

        fullScreen.Leaving += () => ShowModules(false);
    }

    /// <summary>Whether the modules are over the full-screen picture.</summary>
    public bool IsOn { get; private set; }

    /// <summary>The fading of the modules while nothing touches them.</summary>
    internal ModulesFade Fade => fade;

    /// <summary>Gives the picture the window with the modules over it, or gives the window back.</summary>
    public void Toggle()
    {
        if (IsOn)
        {
            fullScreen.Leave();
            return;
        }

        // On another monitor the editor is already beside the picture.
        if (fullScreen.Away is not null) fullScreen.Leave();

        fullScreen.Show(true);
        ShowModules(true);
    }

    /// <summary>Puts the modules over the full-screen picture or takes them off, and says whether it could.</summary>
    public bool ToggleModules()
    {
        if (!fullScreen.IsFullScreen) return false;

        ShowModules(!IsOn);
        return true;
    }

    private void ShowModules(bool on)
    {
        if (on == IsOn) return;

        if (!on)
        {
            IsOn = false;
            PutBack();
            return;
        }

        if (!fullScreen.IsFullScreen || fullScreen.Columns is not { } columns || fullScreen.PreviewBox is not { } picture) return;

        IsOn = true;
        Lift(columns, picture);
    }

    private void Lift(Grid columns, Border picture)
    {
        if (editor.Parent is not Panel pane) return;

        usage.Count(Used.LivePatching);

        home = pane;
        homeIndex = pane.Children.IndexOf(editor);
        homeRow = Grid.GetRow(editor);
        shownBefore = editor.IsVisible;

        pane.Children.Remove(editor);
        Grid.SetColumn(editor, Grid.GetColumn(picture));
        Grid.SetRow(editor, Grid.GetRow(picture));
        Grid.SetRowSpan(editor, Grid.GetRowSpan(picture));
        columns.Children.Insert(columns.Children.IndexOf(picture) + 1, editor);

        editor.IsVisible = true;
        editor.SeeThrough = true;
        fade.Start(editor);
        editor.Focus();

        // Once the canvas has the window's size, so the patch is framed in all of it.
        Dispatcher.UIThread.Post(editor.View.FrameAll, DispatcherPriority.Background);
    }

    private void PutBack()
    {
        fade.Stop();
        editor.SeeThrough = false;

        if (editor.Parent is Panel lifted) lifted.Children.Remove(editor);

        Grid.SetColumn(editor, 0);
        Grid.SetRowSpan(editor, 1);
        Grid.SetRow(editor, homeRow);

        if (home is { } pane) pane.Children.Insert(Math.Min(homeIndex, pane.Children.Count), editor);

        editor.IsVisible = shownBefore;
        home = null;

        if (fullScreen.IsFullScreen) fullScreen.PreviewBox?.Focus();
    }
}
