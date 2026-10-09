using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Bars;
using Flyback.Ui.Controls;
using Flyback.Core.Graph;
using Flyback.Ui;
using Colors = Flyback.Ui.Controls.Colors;
using Flyback.Host;

namespace Flyback.Editor.Knobs;

/// <summary>
/// The end of the knob panel that randomizes it: the die, the way back, and how far
/// and how slowly the knobs go.
/// </summary>
internal sealed class RollCell
{
    private readonly Knob amount = new() { Width = 30, Height = 30, Name = "roll-amount" };
    private readonly Knob glide = new() { Width = 30, Height = 30, Name = "roll-glide" };
    private readonly TextBlock amountReading = Reading();
    private readonly TextBlock glideReading = Reading();
    private readonly Button back;
    private readonly MenuItem learn = new();
    private readonly MenuItem forget = new();
    private readonly Func<MidiBinding, string> explain;

    public RollCell(Func<MidiBinding, string> explain)
    {
        this.explain = explain;

        var dice = ToolbarButtons.Drawn("roll-knobs", Glyphs.Dice(),
            "Randomize every knob that is not held (Ctrl+Shift+K). Right-click to learn a controller button or pad for it.");
        dice.Click += (_, _) => RollRequested?.Invoke();

        back = ToolbarButtons.Drawn("unroll-knobs", Glyphs.Undo(), "Back to where the knobs were before the last randomize.");
        back.Click += (_, _) => BackRequested?.Invoke();

        learn.Click += (_, _) => LearnRequested?.Invoke(true);
        forget.Click += (_, _) => LearnRequested?.Invoke(false);
        dice.ContextMenu = new ContextMenu { Items = { learn, forget } };

        ToolTip.SetTip(amount, "How far a randomize moves each knob from where it is. All the way round is anywhere.");
        ToolTip.SetTip(glide, "How long the knobs take to get there. All the way down jumps.");

        amount.Turned += _ => Tune();
        glide.Turned += _ => Tune();
        amount.Released += () => TuneEnded?.Invoke();
        glide.Released += () => TuneEnded?.Invoke();

        Root = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { dice, back } },
                Labeled("amount", amount, amountReading),
                Labeled("glide", glide, glideReading),
            },
        };

        void Tune()
        {
            Read();
            Tuned?.Invoke(amount.Value, Seconds(glide.Value));
        }
    }

    /// <summary>The knobs not held were asked to go somewhere new.</summary>
    public event Action? RollRequested;

    /// <summary>The knobs were asked back to where they were before the last randomize.</summary>
    public event Action? BackRequested;

    /// <summary>A controller button was asked to be learned for randomizing, or with false, forgotten.</summary>
    public event Action<bool>? LearnRequested;

    /// <summary>How far a randomize reaches and how long it glides were turned: the amount, then seconds.</summary>
    public event Action<double, double>? Tuned;

    /// <summary>The hand came off the amount or the glide.</summary>
    public event Action? TuneEnded;

    public Control Root { get; }

    public void Show(double reach, double seconds, MidiBinding? trigger, bool canGoBack, bool learning)
    {
        amount.Value = reach;
        glide.Value = Turn(seconds);
        back.IsEnabled = canGoBack;

        learn.Header = learning ? "Press a button or a pad on your controller…" : trigger is null ? "Learn MIDI button or pad" : "Learn another button or pad";
        learn.IsEnabled = !learning;
        forget.Header = trigger is { } bound ? $"Forget {explain(bound)}" : "Forget";
        forget.IsVisible = trigger is not null;

        Read();
    }

    /// <summary>The glide knob's seconds, squared so the short glides get most of the turn.</summary>
    private static double Seconds(double turn) => RandomizeSettings.LongestGlide * turn * turn;

    private static double Turn(double seconds) => Math.Sqrt(Math.Clamp(seconds / RandomizeSettings.LongestGlide, 0, 1));

    private void Read()
    {
        amountReading.Text = (amount.Value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        var seconds = Seconds(glide.Value);
        glideReading.Text = seconds < 0.01 ? "jump" : seconds.ToString(seconds < 1 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + " s";
    }

    private static TextBlock Reading() => new()
    {
        FontSize = Text.Caption,
        Foreground = new SolidColorBrush(Colors.Value),
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static StackPanel Labeled(string label, Knob knob, TextBlock reading) => new()
    {
        Spacing = 1,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new TextBlock { Text = label, FontSize = Text.Small, Foreground = new SolidColorBrush(Colors.Label), HorizontalAlignment = HorizontalAlignment.Center },
            knob,
            reading,
        },
    };
}
