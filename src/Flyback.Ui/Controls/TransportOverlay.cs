using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Flyback.Host;

namespace Flyback.Ui.Controls;

/// <summary>
/// The transport over a full-window picture, tucked behind three dots at the top or the
/// bottom center, whichever the knobs do not have: pause, rewind, the seek strip, the loop
/// switch and the sound, in the order the toolbar has them. Bare glyphs on no bar, drawn
/// the way the knobs are, and no numbers.
/// </summary>
/// <remarks>
/// Holds no state of its own: the owner sets <see cref="Paused"/>, <see cref="Muted"/>
/// and <see cref="Sounding"/>, moves the thumb with <see cref="Follow"/>, and hears the clicks.
/// </remarks>
public sealed class TransportOverlay : TuckedAway
{
    private const double Gap = 2;
    private const double Inset = 12;

    /// <summary>How wide the strip is over a picture with room for it.</summary>
    private const double Wide = 360;

    /// <summary>What the buttons beside the strip take: the four of the transport, and the live ones when shown.</summary>
    private double Beside => (4 + (Live ? 3 : 0)) * (ToolSize + Gap) + 12;

    /// <summary>A switch that is on, in the color the knobs' travel and the toolbar's strip are.</summary>
    private static readonly IBrush On = new ImmutableSolidColorBrush(Colors.Attention);

    /// <summary>The class a switch that is on carries.</summary>
    private const string Engaged = "on";

    private readonly Button pauseButton;
    private readonly Button muteButton;
    private readonly Button loopButton;
    private readonly Button patchButton;
    private readonly Button textButton;
    private readonly Button leaveButton;

    public TransportOverlay()
        : this(new StackPanel { Orientation = Orientation.Horizontal, Spacing = Gap })
    {
    }

    private TransportOverlay(StackPanel row)
        : base(row, HorizontalAlignment.Center, VerticalAlignment.Top)
    {
        pauseButton = Tool(Glyphs.Pause(), "Pause or play", () =>
        {
            PauseClicked?.Invoke();
            Shown();
        });
        var rewind = Tool(Glyphs.Rewind(), "Back to the start", () => RewindClicked?.Invoke());
        loopButton = Tool(Glyphs.Loop(), "Play the patch's length round and round", () => LoopClicked?.Invoke());
        muteButton = Tool(Glyphs.Speaker(), "Sound on or off", () => MuteClicked?.Invoke());
        patchButton = Tool(Glyphs.Live(), "The patch over the picture, or the picture alone  (Tab)", () => PatchClicked?.Invoke());
        textButton = Tool(Glyphs.Code(), "The patch as modules or as text  (F2). Text is best with a keyboard.", () => TextClicked?.Invoke());
        leaveButton = Tool(Glyphs.Cross(), "Give the window back  (Esc)", () => LeaveClicked?.Invoke());
        patchButton.Name = "livePatch";
        textButton.Name = "liveText";
        leaveButton.Name = "liveLeave";
        patchButton.IsVisible = textButton.IsVisible = leaveButton.IsVisible = false;

        // After the base's hover and press styles, so a switch that is on stays lit under the pointer.
        var engaged = new Style(x => x
            .OfType<Button>().Class(Engaged)
            .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));

        engaged.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, On));
        Styles.Add(engaged);

        Track.Name = "seekOver";
        Track.Margin = new Thickness(6, 0);
        Track.Sought += seconds => Sought?.Invoke(seconds);
        Track.DoubleTapped += (_, e) => e.Handled = true;

        ToolTip.SetTip(Track, "Drag to move the patch's clock, in the picture and in the sound.");

        row.Children.Add(pauseButton);
        row.Children.Add(rewind);
        row.Children.Add(Track);
        row.Children.Add(loopButton);
        row.Children.Add(muteButton);
        row.Children.Add(patchButton);
        row.Children.Add(textButton);
        row.Children.Add(leaveButton);

        Margin = new Thickness(Inset);

        // The strip opens under the dots, and a click aimed at them is not a seek.
        GuardsArrival = true;
    }

    /// <summary>Stands <paramref name="transport"/> at <paramref name="edge"/> and <paramref name="knobs"/> at the other.</summary>
    public static void Lay(TransportEdge edge, TransportOverlay transport, StageKnobs knobs)
    {
        transport.Edge = edge == TransportEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        knobs.Edge = edge == TransportEdge.Top ? VerticalAlignment.Bottom : VerticalAlignment.Top;
    }

    /// <summary>A picture too narrow for the whole strip gets a shorter one.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = Math.Clamp(availableSize.Width - Beside, 60, Wide);

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (Track.Width != width) Track.Width = width;

        return base.MeasureOverride(availableSize);
    }

    /// <summary>Raised when the sound button is pressed; the owner flips <see cref="Muted"/>.</summary>
    public event Action? MuteClicked;

    /// <summary>Raised when the pause button is pressed; the owner flips <see cref="Paused"/>.</summary>
    public event Action? PauseClicked;

    /// <summary>The patch button was pressed: the patch over the picture, or taken off it.</summary>
    public event Action? PatchClicked;

    /// <summary>The text button was pressed: the patch as text, or as modules again.</summary>
    public event Action? TextClicked;

    /// <summary>The leave button was pressed: the window given back.</summary>
    public event Action? LeaveClicked;

    /// <summary>Whether the live patching buttons are on the strip, for a picture with a patch to put over it.</summary>
    public bool Live
    {
        get => leaveButton.IsVisible;
        set
        {
            patchButton.IsVisible = textButton.IsVisible = leaveButton.IsVisible = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Whether the patch is over the picture, which lights the patch button.</summary>
    public bool PatchOver
    {
        get => patchButton.Classes.Contains(Engaged);
        set => patchButton.Classes.Set(Engaged, value);
    }

    /// <summary>Whether the patch is shown as text, which lights the text button.</summary>
    public bool Coding
    {
        get => textButton.Classes.Contains(Engaged);
        set => textButton.Classes.Set(Engaged, value);
    }

    /// <summary>Raised when the rewind button is pressed.</summary>
    public event Action? RewindClicked;

    /// <summary>A hand put the clock at this many seconds.</summary>
    public event Action<double>? Sought;

    /// <summary>The loop switch was pressed; the owner flips it and says so through <see cref="Follow"/>.</summary>
    public event Action? LoopClicked;

    /// <summary>The strip, for the tests that aim a pointer along it.</summary>
    internal SeekTrack Track { get; } = new(stage: true) { Width = Wide };

    /// <summary>Whether play is held. The pause button shows what a press does next.</summary>
    public bool Paused
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            pauseButton.Content = Face(field ? Glyphs.Play() : Glyphs.Pause());
        }
    }

    /// <summary>Whether the sound is turned off.</summary>
    public bool Muted
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            muteButton.Content = Face(field ? Glyphs.Muted() : Glyphs.Speaker());
        }
    }

    /// <summary>Whether the patch has sound at all, without which there is no sound button.</summary>
    public bool HasSound
    {
        get => muteButton.IsVisible;
        set => muteButton.IsVisible = value;
    }

    /// <summary>Whether there is sound to turn off.</summary>
    public bool Sounding
    {
        get => muteButton.IsEnabled;
        set => muteButton.IsEnabled = value;
    }

    /// <summary>Whether the patch comes round to nought at its end.</summary>
    public bool Looped { get; private set; }

    /// <summary>Whether the strip can be used: not during a take, which is paced by its own samples.</summary>
    public bool CanSeek
    {
        get => Track.IsEnabled;
        set => Track.IsEnabled = value;
    }

    /// <summary>
    /// Moves the thumb to <paramref name="seconds"/> of <paramref name="length"/>, unless a
    /// hand has it. A patch with no length plays on, so it has no strip and no loop.
    /// </summary>
    public void Follow(double seconds, double? length, bool loops)
    {
        Track.IsVisible = loopButton.IsVisible = length is not null;

        if (length is { } end)
        {
            Track.Maximum = end;
            if (!Track.Held) Track.Value = Math.Min(seconds, end);
        }

        if (Looped == loops) return;

        Looped = loops;
        loopButton.Classes.Set(Engaged, loops);
    }
}
