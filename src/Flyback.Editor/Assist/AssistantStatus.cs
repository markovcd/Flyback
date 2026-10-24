using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Assist;

/// <summary>The pill in the assistant's header holding the beacon: working, ready, not set up, or driven.</summary>
internal sealed class AssistantStatus
{
    private static readonly IBrush Amber = new ImmutableSolidColorBrush(Colors.Attention);
    private static readonly IBrush Live = new ImmutableSolidColorBrush(Colors.Feedback);
    private static readonly IBrush LiveInk = new ImmutableSolidColorBrush(Colors.Blend(Colors.Feedback, Avalonia.Media.Colors.White, 0.3));
    private static readonly IBrush LiveGround = new ImmutableSolidColorBrush(Colors.Faded(Colors.Feedback, 0.13));
    private static readonly IBrush Idle = new ImmutableSolidColorBrush(Colors.Node);
    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Colors.Label);

    /// <summary>
    /// Proof that something is still happening.
    /// </summary>
    /// <remarks>
    /// A turn is minutes and most of it is spent waiting on a provider with
    /// nothing to show, so without this the panel cannot be told from one that has
    /// died. A beacon that moves says it in a way a label could not, since a label
    /// can as easily be stale.
    /// </remarks>
    private readonly Ellipse beacon = new()
    {
        Width = 7,
        Height = 7,
        Fill = Live,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock progress = new()
    {
        Name = "progress",
        FontSize = Text.Small,
        FontWeight = FontWeight.Medium,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private DateTime startedAt;
    private int pulse;

    public AssistantStatus()
    {
        var pill = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        pill.Children.Add(beacon);
        pill.Children.Add(progress);

        View = new Border
        {
            Name = "status",
            CornerRadius = new CornerRadius(99),
            Padding = new Thickness(9, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = pill,
        };
    }

    public Border View { get; }

    /// <summary>Ready, or not set up with <paramref name="blocked"/> as the reason.</summary>
    public void Ready(string? blocked)
    {
        beacon.Opacity = 1;
        beacon.Fill = blocked is null ? Text.Muted : Amber;
        progress.Text = blocked is null ? "Ready" : "Not set up";
        progress.Foreground = Ink;
        View.Background = Idle;
        ToolTip.SetTip(View, blocked);
    }

    /// <summary>A program on this computer is editing the open patch.</summary>
    public void Driven(string program)
    {
        beacon.Opacity = 1;
        beacon.Fill = Live;
        progress.Text = "Driven";
        progress.Foreground = LiveInk;
        View.Background = LiveGround;
        ToolTip.SetTip(View, $"{program} is editing the open patch through flyback-cli mcp. Close it there to type here again.");
    }

    /// <summary>A turn has begun; the clock starts from now.</summary>
    public void Start()
    {
        startedAt = DateTime.UtcNow;
        pulse = 0;

        beacon.Fill = Live;
        progress.Foreground = LiveInk;
        View.Background = LiveGround;
    }

    /// <summary>
    /// One tick of the beacon, with <paramref name="done"/> appended to what it says
    /// when hovered. Returns the tick's count since <see cref="Start"/>.
    /// </summary>
    public int Beat(bool stopping, string done)
    {
        // A cosine rather than a blink: something that fades is obviously alive
        // and does not compete with the transcript for attention, which a thing
        // switching on and off twice a second would.
        pulse++;
        beacon.Opacity = 0.25d + 0.75d * ((Math.Cos(pulse * Math.PI / 5d) + 1d) / 2d);

        var elapsed = Spell(DateTime.UtcNow - startedAt);

        progress.Text = $"{(stopping ? "Stopping" : "Working")} · {elapsed}";

        ToolTip.SetTip(View, stopping
            ? $"Stopping — it ends at the next thing the assistant does · {elapsed}"
            : $"Working · {elapsed}{done}");

        return pulse;
    }

    private static string Spell(TimeSpan elapsed) => elapsed.TotalSeconds < 60d
        ? $"{elapsed.TotalSeconds:0}s"
        : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s";
}
