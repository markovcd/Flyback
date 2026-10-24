using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Assist;

/// <summary>
/// The box a message is written in, with the keys that send it, Expand and the
/// send button along its foot, and above it whatever is stopping a send.
/// </summary>
internal sealed class AssistantComposer
{
    private static readonly IBrush Amber = new ImmutableSolidColorBrush(Colors.Attention);
    private static readonly IBrush Chrome = new ImmutableSolidColorBrush(Colors.Panel);
    private static readonly IBrush Rule = new ImmutableSolidColorBrush(Colors.Edge);

    /// <summary>The class the message box and the send button are styled by; see <see cref="Dress"/>.</summary>
    internal const string Composing = "composing";

    private readonly TextBox instruction = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        PlaceholderText = "Describe the patch you want",
        Name = "instruction",
        FontSize = Text.Body,
        MinHeight = 40,
        MaxHeight = 240,
        Padding = new Thickness(0, 2),
        Classes = { Composing },
    };

    private readonly TextBlock footer = new()
    {
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 0, 2, 8),
        Name = "footer",
    };

    private readonly Control sendMark = Glyphs.Send();

    private readonly Control stopMark = Glyphs.Stop();

    /// <summary>
    /// Send and stop, which are one button because they are never both offered: a
    /// turn is either wanted or under way, and the way to interrupt a run belongs
    /// where the hand that started it last was.
    /// </summary>
    private readonly Button send = new()
    {
        Name = "send",
        Width = 34,
        Height = 34,
        Padding = new Thickness(0),
        CornerRadius = new CornerRadius(8),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        IsEnabled = false,
        Classes = { Composing },
    };

    /// <summary>
    /// Has the assistant write the box out in full, in the box: as a change to the
    /// patch on the canvas, or as a new patch's brief when the canvas is empty.
    /// </summary>
    private readonly Button expand = new()
    {
        Content = "Expand",
        FontSize = Text.Small,
        Foreground = Text.Muted,
        Background = Brushes.Transparent,
        Padding = new Thickness(8, 4),
        VerticalAlignment = VerticalAlignment.Center,
        IsEnabled = false,
        Name = "expand-message",
    };

    public AssistantComposer()
    {
        // Tunnelling, and both halves of the gesture answered here: the box would
        // otherwise take Enter for itself on the way back up, and what it does
        // with a modifier held is its business rather than something to bet the
        // one way of sending a message on.
        instruction.AddHandler(InputElement.KeyDownEvent, Typed, RoutingStrategies.Tunnel);

        instruction.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) Edited?.Invoke();
        };

        send.Click += (_, _) => Pressed?.Invoke();
        expand.Click += (_, _) => Expanding?.Invoke();

        var keys = Text.Quiet("Enter to ask · Ctrl+Enter for a new line", Text.Caption);
        keys.VerticalAlignment = VerticalAlignment.Center;
        keys.TextTrimming = TextTrimming.CharacterEllipsis;

        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
        foot.Children.Add(keys);
        foot.Children.Add(expand);
        foot.Children.Add(send);
        Grid.SetColumn(expand, 1);
        Grid.SetColumn(send, 2);

        var writing = new StackPanel { Spacing = 4 };
        writing.Children.Add(instruction);
        writing.Children.Add(foot);

        var field = new Border
        {
            Name = "composer",
            Background = new SolidColorBrush(Colors.Toolbar),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 6, 6),
            Child = writing,
        };

        // A click anywhere in the field is a click in the box.
        field.PointerPressed += (_, _) => instruction.Focus();

        var column = new StackPanel { Margin = new Thickness(10, 10, 10, 10) };
        column.Children.Add(footer);
        column.Children.Add(field);

        View = new Border
        {
            Background = Chrome,
            BorderBrush = Rule,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = column,
        };

        Dress();
    }

    /// <summary>Enter, with no modifier held, in the box.</summary>
    public event Action? Asked;

    /// <summary>The send button, which stops the turn while one runs.</summary>
    public event Action? Pressed;

    /// <summary>The Expand button.</summary>
    public event Action? Expanding;

    /// <summary>What is in the box changed.</summary>
    public event Action? Edited;

    public Border View { get; }

    /// <summary>What is in the box.</summary>
    public string Message
    {
        get => instruction.Text ?? string.Empty;
        set => instruction.Text = value;
    }

    /// <summary>Whether the box refuses typing, as it does while it is being written out.</summary>
    public bool Locked
    {
        set => instruction.IsReadOnly = value;
    }

    /// <summary>Puts <paramref name="text"/> in the box with the caret after it.</summary>
    public void Put(string text)
    {
        instruction.Text = text;
        instruction.CaretIndex = text.Length;
    }

    /// <summary>
    /// Says why a message cannot be sent, on the box and in amber above it, or
    /// nothing where <paramref name="excuse"/> is null.
    /// </summary>
    public void Excuse(string? excuse)
    {
        ToolTip.SetTip(instruction, excuse);

        footer.IsVisible = excuse is not null;

        if (excuse is null) return;

        footer.Text = excuse;
        footer.Foreground = Amber;
    }

    /// <summary>
    /// The send button in whichever of its two jobs applies, and Expand. Dead until
    /// there is something to send, which says what the panel knew and never showed:
    /// an empty box or a missing key is why Enter appeared to do nothing.
    /// </summary>
    public void Show(bool asking, string? blocked)
    {
        var typed = !string.IsNullOrWhiteSpace(instruction.Text);

        send.Content = asking ? stopMark : sendMark;
        send.IsEnabled = asking || (blocked is null && typed);
        expand.IsEnabled = !asking && blocked is null && typed;

        ToolTip.SetTip(expand, "Have the assistant write this out in full, here, for you to edit before sending: "
            + "as a change to the patch on the canvas, or as a new patch's brief when the canvas is empty.");

        ToolTip.SetTip(send, asking
            ? "Stop — it ends at the next thing the assistant does"
            : blocked ?? "Ask  (Enter)");
    }

    /// <summary>
    /// Enter asks; Ctrl+Enter — and Shift+Enter, which every other message box
    /// accepts — breaks the line. The break is put in by hand, because whether a
    /// <see cref="TextBox"/> types a newline for a gesture with a modifier held is
    /// Avalonia's own business.
    /// </summary>
    private void Typed(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        e.Handled = true;

        var breaking = e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (!breaking)
        {
            Asked?.Invoke();
            return;
        }

        var text = instruction.Text ?? string.Empty;
        var from = Math.Clamp(Math.Min(instruction.SelectionStart, instruction.SelectionEnd), 0, text.Length);
        var to = Math.Clamp(Math.Max(instruction.SelectionStart, instruction.SelectionEnd), from, text.Length);

        instruction.Text = string.Concat(text.AsSpan(0, from), "\n", text.AsSpan(to));
        instruction.CaretIndex = from + 1;
    }

    /// <summary>
    /// Undresses the message box, which sits in a field of its own, and fills the
    /// send button. The theme sets both inside their templates, which only a style
    /// reaching the same part can override.
    /// </summary>
    private void Dress()
    {
        var box = new Style(x => x.OfType<TextBox>().Class(Composing).Template().OfType<Border>().Name("PART_BorderElement"));
        box.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
        box.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0)));
        View.Styles.Add(box);

        foreach (var (state, ground, ink) in new[]
                 {
                     (null, Colors.Label, Colors.Window),
                     (":pointerover", Colors.BeamCore, Colors.Window),
                     (":pressed", Colors.Value, Colors.Window),
                     (":disabled", Colors.Node, Colors.Inactive),
                 })
        {
            var style = new Style(x =>
            {
                var button = x.OfType<Button>().Class(Composing);
                if (state is not null) button = button.Class(state);
                return button.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
            });

            style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, new ImmutableSolidColorBrush(ground)));
            style.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, new ImmutableSolidColorBrush(ink)));
            View.Styles.Add(style);
        }
    }
}
