using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Flyback.Ui.Audio;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Windows;

/// <summary>
/// The preview full screen on a monitor of its own, with the knobs and the
/// transport over it (ADR-0129, ADR-0148).
/// </summary>
/// <remarks>
/// Not activated, so the keyboard stays with the editor. Double-clicking the
/// picture or pressing Escape closes it, and closing hands the preview back: the
/// renderer is built again once each way.
/// </remarks>
internal sealed class PictureWindow : Window
{
    private readonly Panel picture = new();

    /// <summary>The knobs over the picture.</summary>
    public StageKnobs Knobs { get; } = new();

    /// <summary>The dots and transport over the top of the picture.</summary>
    public TransportOverlay Transport { get; } = new();

    /// <summary>Ctrl+P was pressed over the picture.</summary>
    public event EventHandler? PauseRequested;

    /// <summary>F3 was pressed over the picture.</summary>
    public event EventHandler? StatsRequested;

    /// <summary>The line saying how the picture is drawn, which the editor shows or puts away.</summary>
    public StatsOverlay Stats { get; }

    /// <param name="heard">Whether the patch has sound, for the stats line.</param>
    internal PictureWindow(Screen screen, PreviewHost preview, IAudioEngine sound, Func<bool> heard)
    {
        Title = "Flyback picture";
        Background = Brushes.Black;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 160;
        Height = 90;

        Stats = new StatsOverlay(preview, sound, heard);

        picture.Children.Add(preview);
        picture.Children.Add(Stats);
        picture.Children.Add(Knobs);
        picture.Children.Add(Transport);

        Content = picture;

        PictureTaps.Attach(this, () => PauseRequested?.Invoke(this, EventArgs.Empty), Close);

        KeyDown += (_, e) =>
        {
            switch (StageKeys.Read(e.Key, e.KeyModifiers))
            {
                case StageKey.Pause:
                    PauseRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case StageKey.Stats:
                    StatsRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case StageKey.Leave or StageKey.FullScreen:
                    Close();
                    break;

                default:
                    return;
            }

            e.Handled = true;
        };

        // Moved only once it is open: Windows tells a hidden window nothing when it
        // crosses to a monitor of another scale, and it would draw at the old one.
        Opened += (_, _) =>
        {
            Position = screen.Bounds.Position;
            WindowState = WindowState.FullScreen;
        };
    }

    /// <summary>Lets go of the preview, so whoever lent it can take it back.</summary>
    protected override void OnClosed(EventArgs e)
    {
        picture.Children.Clear();
        Content = null;

        base.OnClosed(e);
    }
}
