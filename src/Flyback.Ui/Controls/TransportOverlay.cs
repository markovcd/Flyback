using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>
/// Sound, rewind and pause over a full-window picture, tucked behind three
/// dots in the bottom right corner. Bare glyphs on no bar, drawn the way the knobs are.
/// </summary>
/// <remarks>
/// Holds no state of its own: the owner sets <see cref="Paused"/>, <see cref="Muted"/>
/// and <see cref="Sounding"/>, and hears the clicks.
/// </remarks>
public sealed class TransportOverlay : TuckedAway
{
    private const double Gap = 2;
    private const double Inset = 12;

    /// <summary>How far in from the right edge the open transport reaches.</summary>
    public const double Span = Inset + 3 * ToolSize + 2 * Gap;

    private readonly Button muteButton;
    private readonly Button pauseButton;

    private bool paused;
    private bool muted;

    public TransportOverlay()
        : this(new StackPanel { Orientation = Orientation.Horizontal, Spacing = Gap })
    {
    }

    private TransportOverlay(StackPanel buttons)
        : base(buttons, HorizontalAlignment.Right)
    {
        muteButton = Tool(Glyphs.Speaker(), "Sound on or off", () => MuteClicked?.Invoke());
        pauseButton = Tool(Glyphs.Pause(), "Pause or play", () =>
        {
            PauseClicked?.Invoke();
            Shown();
        });

        var rewind = Tool(Glyphs.Rewind(), "Back to the start", () => RewindClicked?.Invoke());

        // Pause is last: it sits under the three dots, so it is the one a click
        // aimed at the dots lands on, and a stray pause costs less than a rewind.
        buttons.Children.Add(muteButton);
        buttons.Children.Add(rewind);
        buttons.Children.Add(pauseButton);

        Margin = new Thickness(Inset);
    }

    /// <summary>Raised when the sound button is pressed; the owner flips <see cref="Muted"/>.</summary>
    public event Action? MuteClicked;

    /// <summary>Raised when the pause button is pressed; the owner flips <see cref="Paused"/>.</summary>
    public event Action? PauseClicked;

    /// <summary>Raised when the rewind button is pressed.</summary>
    public event Action? RewindClicked;

    /// <summary>Whether play is held. The pause button shows what a press does next.</summary>
    public bool Paused
    {
        get => paused;
        set
        {
            if (paused == value) return;

            paused = value;
            pauseButton.Content = Face(paused ? Glyphs.Play() : Glyphs.Pause());
        }
    }

    /// <summary>Whether the sound is turned off.</summary>
    public bool Muted
    {
        get => muted;
        set
        {
            if (muted == value) return;

            muted = value;
            muteButton.Content = Face(muted ? Glyphs.Muted() : Glyphs.Speaker());
        }
    }

    /// <summary>Whether there is sound to turn off.</summary>
    public bool Sounding
    {
        get => muteButton.IsEnabled;
        set => muteButton.IsEnabled = value;
    }
}
