using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Keys;

/// <summary>
/// The row of keys along the foot of the window that a finger plays a MIDI In with: an
/// octave of a piano, or the home row of the patch's scale, with two buttons that move it
/// by octaves.
/// </summary>
/// <remarks>
/// Draws the keys it is shown and reports what the hand does by each key's browser name
/// (<c>KeyZ</c>), so a key on the screen and the computer key it stands for are one note
/// to the <see cref="Flyback.Ui.Midi.MidiHub"/>. The window decides when the row is up.
/// </remarks>
internal sealed class Keybed : Border
{
    /// <summary>How tall a white key is: a finger's target with room to spare. A sharp is shorter, over the gap between two.</summary>
    public const double KeyHeight = 64;

    private const double SharpHeight = 40;

    /// <summary>The columns a white key spans; a sharp spans half as many, astride the line between two whites.</summary>
    private const int Span = 4;

    // Immutable, since a static is shared across threads and a SolidColorBrush belongs to the one that made it.
    private static readonly IBrush White = new ImmutableSolidColorBrush(Color.FromRgb(0xE9, 0xE6, 0xDF));
    private static readonly IBrush Black = new ImmutableSolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x47));
    private static readonly IBrush Down = new ImmutableSolidColorBrush(Colors.Attention);
    private static readonly IBrush OnWhite = new ImmutableSolidColorBrush(Colors.Window);
    private static readonly IBrush OnBlack = new ImmutableSolidColorBrush(Colors.Label);

    private readonly Grid row = new() { ColumnSpacing = 2 };

    /// <summary>The name of the lowest key's note, between the octave buttons.</summary>
    private readonly TextBlock lowest = new()
    {
        Name = "keybed-lowest",
        MinWidth = 30,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = Text.Body,
        Foreground = new SolidColorBrush(Colors.Label),
    };

    private readonly Dictionary<string, Border> keys = new(StringComparer.Ordinal);

    /// <summary>Which key each pointer is holding down, so a chord of fingers lets go one at a time.</summary>
    private readonly Dictionary<int, string> held = [];

    public Keybed()
    {
        Name = "keybed";
        Background = new SolidColorBrush(Colors.Panel);
        BorderBrush = new SolidColorBrush(Colors.Edge);
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(8, 6);

        var octave = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        octave.Children.Add(OctaveButton("octave-down", "Octave down", Glyphs.Minus(), -1));
        octave.Children.Add(lowest);
        octave.Children.Add(OctaveButton("octave-up", "Octave up", Glyphs.Add(), 1));

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(row, 1);
        columns.Children.Add(octave);
        columns.Children.Add(row);

        Child = columns;
    }

    /// <summary>A key went down, by its browser name.</summary>
    public event Action<string>? Struck;

    /// <summary>A key came up, by its browser name.</summary>
    public event Action<string>? Released;

    /// <summary>The octave button was pressed: up by one, or down by one.</summary>
    public event Action<int>? Shifted;

    /// <summary>The keys on the row, left to right.</summary>
    public IReadOnlyList<ScreenKey> Row { get; private set; } = [];

    /// <summary>Lays <paramref name="keys"/> out left to right, the lowest first, letting go of whatever was held.</summary>
    public void Lay(IReadOnlyList<ScreenKey> keys)
    {
        Release();

        row.Children.Clear();
        row.ColumnDefinitions.Clear();
        this.keys.Clear();
        Row = keys;
        lowest.Text = keys.Count > 0 ? keys[0].Name : string.Empty;

        var whites = keys.Count(key => !key.Sharp);

        for (var column = 0; column < whites * Span; column++)
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        var placed = 0;

        foreach (var key in keys)
        {
            var control = Key(key);

            if (key.Sharp)
            {
                Grid.SetColumn(control, Math.Max(0, placed * Span - Span / 2));
                Grid.SetColumnSpan(control, Span / 2);
                control.ZIndex = 1;
            }
            else
            {
                Grid.SetColumn(control, placed * Span);
                Grid.SetColumnSpan(control, Span);
                placed++;
            }

            row.Children.Add(control);
            this.keys[key.Code] = control;
        }
    }

    /// <summary>Lets every held key go, as when the row is put away under a finger.</summary>
    public void Release()
    {
        foreach (var pointer in held.Keys.ToList()) Lift(pointer);
    }

    private Border Key(ScreenKey key)
    {
        var ink = key.Sharp ? OnBlack : OnWhite;

        var note = new TextBlock
        {
            Text = key.Name,
            FontSize = Text.Small,
            FontWeight = FontWeight.SemiBold,
            Foreground = ink,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var letter = new TextBlock
        {
            Text = key.Letter,
            FontSize = Text.Micro,
            Foreground = ink,
            Opacity = 0.6,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
        labels.Children.Add(note);
        labels.Children.Add(letter);

        var control = new Border
        {
            Name = $"key-{key.Code}",
            Height = key.Sharp ? SharpHeight : KeyHeight,
            VerticalAlignment = VerticalAlignment.Top,
            Background = key.Sharp ? Black : White,
            CornerRadius = new CornerRadius(0, 0, 6, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = labels,
        };

        AutomationProperties.SetName(control, key.Name);

        control.PointerPressed += (_, e) =>
        {
            if (held.ContainsKey(e.Pointer.Id)) return;

            held[e.Pointer.Id] = key.Code;
            control.Background = Down;
            note.Foreground = letter.Foreground = OnWhite;
            e.Handled = true;
            Struck?.Invoke(key.Code);
        };

        control.PointerReleased += (_, e) => Lift(e.Pointer.Id);
        control.PointerCaptureLost += (_, e) => Lift(e.Pointer.Id);

        return control;
    }

    /// <summary>Lets go of what <paramref name="pointer"/> holds, if anything.</summary>
    private void Lift(int pointer)
    {
        if (!held.Remove(pointer, out var code)) return;

        if (keys.TryGetValue(code, out var control))
        {
            var key = Row.First(candidate => candidate.Code == code);
            var ink = key.Sharp ? OnBlack : OnWhite;

            control.Background = key.Sharp ? Black : White;

            foreach (var label in ((StackPanel)control.Child!).Children.OfType<TextBlock>()) label.Foreground = ink;
        }

        Released?.Invoke(code);
    }

    private Button OctaveButton(string name, string said, Control glyph, int octaves)
    {
        var button = new Button
        {
            Name = name,
            Content = glyph,
            Width = 44,
            Height = 44,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        AutomationProperties.SetName(button, said);
        ToolTip.SetTip(button, $"{said}: move the keys an octave.");
        button.Click += (_, _) => Shifted?.Invoke(octaves);

        return button;
    }
}
