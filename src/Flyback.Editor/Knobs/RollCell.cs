using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core.Graph;
using Flyback.Editor.Controls;
using Flyback.Host;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Knobs;

/// <summary>
/// The end of the knob panel that randomizes it: the die, ringed as far as the amount
/// reaches, and the way back with how many steps it holds. Amount, glide and the
/// controller button are in the die's flyout, opened by a right-click or a long press.
/// </summary>
internal sealed class RollCell
{
    private readonly AmountRing ring = new() { Width = 40, Height = 40 };
    private readonly Button dice;
    private readonly Button back;
    private readonly TextBlock backs = new() { FontSize = Text.Small, VerticalAlignment = VerticalAlignment.Center };

    private readonly Flyout settings;
    private readonly Slider amount = Tuner("roll-amount");
    private readonly Slider glide = Tuner("roll-glide");
    private readonly TextBlock amountReading = Reading();
    private readonly TextBlock glideReading = Reading();
    private readonly TextBlock bound = new() { FontSize = Text.Small, Foreground = new SolidColorBrush(Colors.Value), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button learn = Small("roll-learn");
    private readonly Button forget = Small("roll-forget");
    private readonly Func<MidiBinding, string> explain;

    /// <summary>Set while <see cref="Show"/> moves the sliders, so the move is not taken for a hand.</summary>
    private bool showing;

    public RollCell(Func<MidiBinding, string> explain)
    {
        this.explain = explain;

        dice = ToolbarButtons.Drawn("roll-knobs", new Grid { Children = { ring, Centered(Glyphs.Dice()) } }, string.Empty);
        dice.Width = 40;
        dice.Height = 40;
        dice.Padding = new Thickness(0);
        dice.Click += (_, _) => RollRequested?.Invoke();

        back = ToolbarButtons.Drawn(
            "unroll-knobs",
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center, Children = { Glyphs.History(), backs } },
            string.Empty);
        back.Width = 40;
        back.Height = 22;
        back.Padding = new Thickness(0);
        back.Click += (_, _) => BackRequested?.Invoke();

        learn.Click += (_, _) =>
        {
            settings!.Hide();
            LearnRequested?.Invoke(true);
        };
        forget.Click += (_, _) => LearnRequested?.Invoke(false);
        forget.Content = "Forget";

        amount.ValueChanged += (_, _) => Tune();
        glide.ValueChanged += (_, _) => Tune();

        settings = new Flyout
        {
            Placement = PlacementMode.Top,
            Content = new StackPanel
            {
                Name = "roll-settings",
                Width = 220,
                Spacing = 10,
                Children =
                {
                    new DockPanel
                    {
                        Children =
                        {
                            Docked(new TextBlock { Text = "Ctrl+Shift+K", FontSize = Text.Small, Foreground = Text.Muted }, Dock.Right),
                            new TextBlock { Text = "Randomize", FontSize = Text.Body, Foreground = new SolidColorBrush(Colors.Bright) },
                        },
                    },
                    Row("Amount", amountReading, amount),
                    Row("Glide", glideReading, glide),
                    new DockPanel
                    {
                        Children =
                        {
                            Docked(new TextBlock { Text = "MIDI", FontSize = Text.Small, Foreground = new SolidColorBrush(Colors.Label), Margin = new Thickness(0, 0, 8, 0) }, Dock.Left),
                            bound,
                        },
                    },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { learn, forget } },
                },
            },
        };

        settings.Closed += (_, _) => TuneEnded?.Invoke();
        dice.ContextFlyout = settings;

        Root = new Border
        {
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(8, 0, 0, 0),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Spacing = 3, Children = { dice, back } },
        };

        void Tune()
        {
            Read();
            if (!showing) Tuned?.Invoke(amount.Value, Seconds(glide.Value));
        }
    }

    /// <summary>The knobs not held were asked to go somewhere new.</summary>
    public event Action? RollRequested;

    /// <summary>The knobs were asked back to where they were before the last randomize.</summary>
    public event Action? BackRequested;

    /// <summary>A controller button was asked to be learned for randomizing, or with false, forgotten.</summary>
    public event Action<bool>? LearnRequested;

    /// <summary>How far a randomize reaches and how long it glides were changed: the amount, then seconds.</summary>
    public event Action<double, double>? Tuned;

    /// <summary>The settings were put away, so what they were changed to can be kept.</summary>
    public event Action? TuneEnded;

    public Control Root { get; }

    /// <summary>The flyout holding the amount, the glide and the controller button.</summary>
    internal Flyout Settings => settings;

    /// <param name="steps">How many randomizes back can step through.</param>
    public void Show(double reach, double seconds, MidiBinding? trigger, int steps, bool learning)
    {
        showing = true;
        amount.Value = reach;
        glide.Value = Turn(seconds);
        showing = false;

        ring.Value = reach;
        back.IsEnabled = steps > 0;
        backs.Text = steps.ToString(CultureInfo.InvariantCulture);
        ToolTip.SetTip(back, steps == 0
            ? "Back to where the knobs were before the last randomize. There is none yet."
            : $"Back to where the knobs were before the last randomize; {steps} to go back through.");

        bound.Text = learning ? "Press a button or a pad…" : trigger is { } on ? explain(on) : "None";
        learn.Content = trigger is null ? "Learn" : "Learn another";
        learn.IsEnabled = !learning;
        forget.IsVisible = trigger is not null;

        Read();
    }

    /// <summary>The glide slider's seconds, squared so the short glides get most of its travel.</summary>
    private static double Seconds(double turn) => RandomizeSettings.LongestGlide * turn * turn;

    private static double Turn(double seconds) => Math.Sqrt(Math.Clamp(seconds / RandomizeSettings.LongestGlide, 0, 1));

    private void Read()
    {
        var reach = (amount.Value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        var seconds = Seconds(glide.Value);
        var travel = seconds < 0.01 ? "a jump" : seconds.ToString(seconds < 1 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + " s";

        amountReading.Text = reach;
        glideReading.Text = travel;
        ring.Value = amount.Value;

        ToolTip.SetTip(dice, $"Randomize every knob that is not held, within {reach} of where it is, gliding {(seconds < 0.01 ? "in a jump" : "for " + travel)} (Ctrl+Shift+K). "
            + "Right-click or hold for the amount, the glide and a controller button.");
    }

    private static Slider Tuner(string name) => new() { Name = name, Minimum = 0, Maximum = 1 };

    private static TextBlock Reading() => new()
    {
        FontSize = Text.Small,
        FontFamily = Text.Mono,
        Foreground = new SolidColorBrush(Colors.Value),
    };

    private static Button Small(string name) => new() { Name = name, FontSize = Text.Small, Padding = new Thickness(8, 3) };

    private static StackPanel Row(string label, TextBlock reading, Slider slider) => new()
    {
        Children =
        {
            new DockPanel
            {
                Children =
                {
                    Docked(reading, Dock.Right),
                    new TextBlock { Text = label, FontSize = Text.Small, Foreground = new SolidColorBrush(Colors.Label) },
                },
            },
            slider,
        },
    };

    private static Control Docked(Control control, Dock side)
    {
        DockPanel.SetDock(control, side);
        return control;
    }

    private static Control Centered(Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Center;
        control.VerticalAlignment = VerticalAlignment.Center;
        return control;
    }
}
