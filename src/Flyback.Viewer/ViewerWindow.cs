using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Plugins.Audio;

namespace Flyback.Viewer;

/// <summary>
/// A patch playing in a window with nothing else in it: the picture, the sound, and a
/// toolbar that shows itself when the pointer comes near.
/// </summary>
/// <remarks>
/// Handed an already-opened patch, a device and settled options, and never a path: the
/// program reads the file and opens the device, so the window can be built headless.
/// Nothing here is written anywhere.
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "The player is disposed when the window closes.")]
internal sealed partial class ViewerWindow : Window
{
    /// <summary>The largest a window opens at when it was not told a size.</summary>
    private static readonly PixelSize LargestStart = new(1280, 720);

    private readonly PreviewHost? preview;
    private readonly Border previewBox;
    private readonly ViewerPlayer player;

    /// <summary>What the window was before it went full screen, for a maximized one does not return to normal.</summary>
    private WindowState stateBefore = WindowState.Normal;

    public ViewerWindow(Opened opened, IAudioDevice? device, ViewerOptions options)
    {
        Title = options.Title ?? "Flyback Viewer";
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var start = options.Window ?? Fitted(options.Size);

        Width = start.Width;
        Height = start.Height;

        if (options.Maximized) WindowState = WindowState.Maximized;
        if (options.FullScreen) WindowState = WindowState.FullScreen;
        if (options.Top) Topmost = true;

        // Focus stays where it was: the window is shown without being activated.
        ShowActivated = !options.Background;

        // No surface at all without a picture: a PreviewHost in the tree renders on a timer.
        if (options.Video) preview = new PreviewHost();

        player = new ViewerPlayer(opened, device, options, preview);

        previewBox = new Border { Background = Brushes.Black, Child = preview };

        // Double-click the picture and it takes the screen; double-click or Escape
        // puts it back. The editor does the same with its own preview, but that one
        // zeroes grid tracks around a control that must not be reparented, and this
        // window has no tracks — so nothing is shared with it.
        previewBox.DoubleTapped += (_, e) =>
        {
            ToggleFullScreen();
            e.Handled = true;
        };

        var layout = new Panel();

        layout.Children.Add(previewBox);

        if (!options.NoOverlay) layout.Children.Add(BuildOverlay());

        Content = layout;

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || WindowState != WindowState.FullScreen) return;

            ToggleFullScreen();
            e.Handled = true;
        };

        Opened += (_, _) =>
        {
            player.Begin();

            // A device that refused to start leaves nothing for the speaker button to do.
            if (Overlay is { } overlay) overlay.Sounding = player.Sounding;
        };

        player.Finished += Close;

        Closed += (_, _) => player.Dispose();
    }

    /// <summary>The player behind the window, for whoever drives it.</summary>
    internal ViewerPlayer Player => player;

    /// <summary>The picture's surface, or null for a run with no picture.</summary>
    internal PreviewHost? Preview => preview;

    /// <summary>The transport over the picture, or null for a run that asked for none.</summary>
    internal TransportOverlay? Overlay { get; private set; }

    private TransportOverlay BuildOverlay()
    {
        var overlay = Overlay = new TransportOverlay(this)
        {
            Muted = player.Muted,
            Paused = player.Paused,
            Sounding = player.Sounding,
        };

        overlay.MuteClicked += () =>
        {
            player.Mute(!player.Muted);
            overlay.Muted = player.Muted;
        };

        overlay.PauseClicked += () =>
        {
            player.Toggle();
            overlay.Paused = player.Paused;
            overlay.Sounding = player.Sounding;
        };

        overlay.RewindClicked += player.Rewind;

        return overlay;
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
