using Avalonia.Controls;
using Avalonia.Threading;
using Flyback.Editor.Canvas;
using Flyback.Editor.Hubs;
using Flyback.Editor.Notices;
using Flyback.Editor.Statistics;
using Flyback.Host;

namespace Flyback.Editor.Windows;

/// <summary>
/// The patch drawn over a full-screen picture and edited there, as modules or as text,
/// for playing a patch live in front of people.
/// </summary>
/// <remarks>
/// Whichever view the patch is showing, the canvas or the text, is lifted out of its pane
/// into the preview's cell, just above the picture, rather than the picture being moved:
/// the preview is a GPU surface that rebuilds when reparented, and the views move for
/// free. The transport and the knobs over the picture stay above it. F2 swaps the view
/// that is lifted, as it swaps the views in their pane.
/// </remarks>
internal sealed class LivePatching : IReactTo<ViewChanged>
{
    private readonly FullScreenPreview fullScreen;
    private readonly NodeEditor editor;
    private readonly SourceView source;
    private readonly Document document;
    private readonly Usage usage;
    private readonly IdleFade fade;

    private Control? lifted;
    private Panel? home;
    private int homeIndex;
    private int homeRow;

    public LivePatching(FullScreenPreview fullScreen, NodeEditor editor, SourceView source, Document document, Usage usage)
    {
        this.fullScreen = fullScreen;
        this.editor = editor;
        this.source = source;
        this.document = document;
        this.usage = usage;
        fade = new IdleFade(() => lifted == editor && editor.Gestures.Gesturing);

        fullScreen.Leaving += () => ShowPatch(false);
    }

    /// <summary>Whether the patch is over the full-screen picture.</summary>
    public bool IsOn => lifted is not null;

    /// <summary>Whether it is the patch's text that is over the picture, which takes the keys typed.</summary>
    public bool Coding => lifted == source;

    /// <summary>The fading of the patch while nothing touches it.</summary>
    internal IdleFade Fade => fade;

    /// <summary>How the patch looks over the picture, and when it fades.</summary>
    public LivePatchingLook Look
    {
        get => look;
        set
        {
            look = value;
            editor.GlassOpacity = value.ModuleOpacity / 100.0;
            source.BandOpacity = value.TextOpacity / 100.0;

            // A fade under way restarts with the new wait.
            if (lifted is { } view) StartFade(view);
        }
    }

    private LivePatchingLook look = new();

    /// <summary>Gives the picture the window with the patch over it, or gives the window back.</summary>
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
        ShowPatch(true);
    }

    /// <summary>
    /// Puts the patch over the full-screen picture or takes it off, and says whether it could.
    /// Over the text, Tab is the text's own.
    /// </summary>
    public bool ToggleOverPicture()
    {
        if (!fullScreen.IsFullScreen || Coding) return false;

        ShowPatch(!IsOn);
        return true;
    }

    public Task On(ViewChanged notice)
    {
        if (IsOn)
        {
            PutBack();
            Lift();
        }

        return Task.CompletedTask;
    }

    private void ShowPatch(bool on)
    {
        if (on == IsOn) return;

        if (on)
        {
            usage.Count(Used.LivePatching);
            Lift();
        }
        else
        {
            PutBack();
            if (fullScreen.IsFullScreen) fullScreen.PreviewBox?.Focus();
        }
    }

    private void Lift()
    {
        if (fullScreen.Columns is not { } columns || fullScreen.PreviewBox is not { } picture) return;

        Control view = document.ShowingCode ? source : editor;

        if (view.Parent is not Panel pane) return;

        lifted = view;
        home = pane;
        homeIndex = pane.Children.IndexOf(view);
        homeRow = Grid.GetRow(view);

        pane.Children.Remove(view);
        Grid.SetColumn(view, Grid.GetColumn(picture));
        Grid.SetRow(view, Grid.GetRow(picture));
        Grid.SetRowSpan(view, Grid.GetRowSpan(picture));
        columns.Children.Insert(columns.Children.IndexOf(picture) + 1, view);

        if (view == editor)
        {
            editor.SeeThrough = true;

            // Once the canvas has the window's size, so the patch is framed in all of it.
            Dispatcher.UIThread.Post(editor.View.FrameAll, DispatcherPriority.Background);
        }
        else
        {
            source.SeeThrough = true;
        }

        StartFade(view);
        view.Focus();
    }

    private void StartFade(Control view)
    {
        if (view == editor) fade.Start(editor, TimeSpan.FromSeconds(look.ModuleFadeSeconds));
        else fade.Start(source, TimeSpan.FromSeconds(look.TextFadeSeconds), look.TextWakesOnPointer);
    }

    private void PutBack()
    {
        if (lifted is not { } view) return;

        fade.Stop();
        editor.SeeThrough = false;
        source.SeeThrough = false;

        if (view.Parent is Panel over) over.Children.Remove(view);

        Grid.SetColumn(view, 0);
        Grid.SetRowSpan(view, 1);
        Grid.SetRow(view, homeRow);

        if (home is { } pane) pane.Children.Insert(Math.Min(homeIndex, pane.Children.Count), view);

        lifted = null;
        home = null;
    }
}
