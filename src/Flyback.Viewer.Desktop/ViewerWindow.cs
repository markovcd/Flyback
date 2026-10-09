using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.Ui;
using Flyback.Ui.Controls;
using Flyback.Host;

namespace Flyback.Viewer.Desktop;

/// <summary>
/// A patch playing in a window with nothing else in it: the picture, the sound, and a
/// toolbar that shows itself when the pointer comes near.
/// </summary>
/// <remarks>
/// A patch with no picture, or a run with no video, is the toolbar alone: the window
/// fits it, and there is no surface to draw and no full screen to take.
/// <para>
/// Built by <see cref="ViewerServices"/> from an already-opened patch, a device and
/// settled options, and never a path: the program reads the file and opens the device,
/// so the window can be built headless. Nothing here is written anywhere.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "The player is disposed when the window closes.")]
internal sealed class ViewerWindow : Window
{
    /// <summary>The largest a window opens at when it was not told a size.</summary>
    private static readonly PixelSize LargestStart = new(1280, 720);

    /// <summary>How wide a window of buttons alone is, so its title bar has room for the title.</summary>
    private const double ToolbarWidth = 560;

    /// <summary>How often the seek bar follows the clock.</summary>
    private static readonly TimeSpan Follow = TimeSpan.FromMilliseconds(100);

    private readonly PreviewHost? preview;
    private readonly Border previewBox;

    /// <summary>What the window was before it went full screen, for a maximized one does not return to normal.</summary>
    private WindowState stateBefore = WindowState.Normal;

    /// <param name="preview">The picture's surface, or null where there is no picture to show.</param>
    public ViewerWindow(ViewerLaunch launch, ViewerPlayer player, PreviewHost? preview)
    {
        var options = launch.Options;

        Title = options.Title ?? "Flyback Viewer";
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var pictured = preview is not null;
        var toolbar = !pictured && !options.NoOverlay;

        if (toolbar)
        {
            SizeToContent = SizeToContent.Height;
            Width = ToolbarWidth;
            CanResize = false;
        }
        else
        {
            var start = options.Window ?? Fitted(options.Size);

            Width = start.Width;
            Height = start.Height;

            if (options.Maximized) WindowState = WindowState.Maximized;
            if (options.FullScreen && pictured) WindowState = WindowState.FullScreen;
        }

        if (options.Top) Topmost = true;

        // Focus stays where it was: the window is shown without being activated.
        ShowActivated = !options.Background;

        this.preview = preview;
        Player = player;

        previewBox = new Border { Background = Brushes.Black, Child = preview };

        // Double-click the picture or press F11 and it takes the screen; either
        // again, or Escape, puts it back. The editor does the same with its own
        // preview, but that one zeroes grid tracks around a control that must not be reparented, and this
        // window has no tracks — so nothing is shared with it.
        if (pictured) PictureTaps.Attach(previewBox, TogglePause, ToggleFullScreen);

        if (toolbar)
        {
            var knobs = BuildKnobs(options.KnobGrid);
            var overlay = BuildOverlay();

            knobs.Pin();
            overlay.Pin();

            Content = options.Transport == TransportEdge.Top
                ? new StackPanel { Children = { overlay, knobs } }
                : new StackPanel { Children = { knobs, overlay } };
        }
        else
        {
            var layout = new Panel();

            layout.Children.Add(previewBox);

            if (preview is not null)
            {
                Stats = new StatsOverlay(preview, player.Audio, () => player.Patch.Reaches().Sound) { IsVisible = options.Stats };
                layout.Children.Add(Stats);
            }

            if (!options.NoOverlay)
            {
                var knobs = BuildKnobs(options.KnobGrid);
                var overlay = BuildOverlay();

                TransportOverlay.Lay(options.Transport, overlay, knobs);

                layout.Children.Add(knobs);
                layout.Children.Add(overlay);
            }

            Content = layout;
        }

        KeyDown += (_, e) =>
        {
            switch (StageKeys.Read(e.Key, e.KeyModifiers))
            {
                case StageKey.Leave when WindowState == WindowState.FullScreen:
                case StageKey.FullScreen when preview is not null:
                    ToggleFullScreen();
                    break;

                case StageKey.Stats when Stats is not null:
                    Stats.Toggle();
                    break;

                case StageKey.Pause:
                    TogglePause();
                    break;

                default:
                    var bare = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) == 0;
                    if (!bare || !player.KeyDown(e.Key)) return;
                    break;
            }

            e.Handled = true;
        };

        KeyUp += (_, e) => player.KeyUp(e.Key);

        // A key held as the window loses the keyboard is never seen coming up.
        Deactivated += (_, _) => player.AllOff();

        Opened += (_, _) =>
        {
            player.Begin();

            // A device that refused to start leaves nothing for the speaker button to do.
            if (Overlay is { } overlay) overlay.Sounding = player.Sounding;
        };

        player.Finished += Close;

        // The player stops itself at the end of the patch's length, so the buttons follow it.
        if (Overlay is not null)
        {
            var follow = new DispatcherTimer(DispatcherPriority.Background) { Interval = Follow };

            follow.Tick += (_, _) => Followed();
            Opened += (_, _) => follow.Start();
            Closed += (_, _) => follow.Stop();
        }
    }

    /// <summary>The player behind the window, for whoever drives it.</summary>
    internal ViewerPlayer Player { get; }

    /// <summary>The picture's surface, or null for a run with no picture.</summary>
    internal PreviewHost? Preview => preview;

    /// <summary>The transport over the picture, or null for a run that asked for none.</summary>
    internal TransportOverlay? Overlay { get; private set; }

    /// <summary>The line saying how the picture is drawn, or null for a run with no picture.</summary>
    internal StatsOverlay? Stats { get; }

    /// <summary>The knobs over the picture, or null for a run that asked for no overlay.</summary>
    internal StageKnobs? Knobs { get; private set; }

    private StageKnobs BuildKnobs(KnobGrid grid)
    {
        var knobs = Knobs = new StageKnobs { KnobGrid = grid };

        knobs.Show(Player.Patch);
        knobs.IsVisible = knobs.Any;
        knobs.Turning += Player.Turn;

        Player.Turned += (id, value) => Dispatcher.UIThread.Post(() => knobs.Move(id, value));

        return knobs;
    }

    private TransportOverlay BuildOverlay()
    {
        var overlay = Overlay = new TransportOverlay()
        {
            Muted = Player.Muted,
            Paused = Player.Paused,
            Sounding = Player.Sounding,
            HasSound = Player.Patch.Reaches().Sound,
        };

        overlay.MuteClicked += () =>
        {
            Player.Mute(!Player.Muted);
            overlay.Muted = Player.Muted;
        };

        overlay.PauseClicked += TogglePause;

        overlay.RewindClicked += Player.Rewind;
        overlay.Sought += Player.SeekTo;
        overlay.LoopClicked += () =>
        {
            Player.Looped = !Player.Looped;
            Followed();
        };

        overlay.Follow(Player.Time, Player.Length, Player.Looped);

        return overlay;
    }

    /// <summary>Puts the strip and the pause button where the player is.</summary>
    internal void Followed()
    {
        if (Overlay is not { } overlay) return;

        overlay.Follow(Player.Time, Player.Length, Player.Looped);
        overlay.Paused = Player.Paused;
    }

    private void TogglePause()
    {
        Player.Toggle();

        if (Overlay is not { } overlay) return;

        overlay.Paused = Player.Paused;
        overlay.Sounding = Player.Sounding;
    }

    /// <summary>The picture, fitted inside <see cref="LargestStart"/> at its own shape.</summary>
    private static PixelSize Fitted(PixelSize picture)
    {
        var scale = Math.Min(1.0, Math.Min(
            (double)LargestStart.Width / picture.Width,
            (double)LargestStart.Height / picture.Height));

        return new PixelSize(
            Math.Max((int)(picture.Width * scale), 160),
            Math.Max((int)(picture.Height * scale), 90));
    }

    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = stateBefore;
            return;
        }

        stateBefore = WindowState;
        WindowState = WindowState.FullScreen;
    }
}
