using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Inspect;

/// <summary>
/// The plate's action buttons as something else can show them: a word and a fresh glyph for
/// each, and a press that is the button's own. The rows decide what applies and what it does;
/// the header and the menu only mirror them.
/// </summary>
internal static class PlateActions
{
    /// <summary>The word a finger is shown under an action's glyph, and the glyph, by the button's name.</summary>
    public static (string Label, Func<Control> Glyph)? Of(string? name) => name switch
    {
        "switch-modules" or "switch-group" => ("Bypass", Glyphs.Switch),
        "group" => ("Group", Glyphs.Group),
        "open-groups" or "open-group" => ("Open box", Glyphs.OpenBox),
        "close-groups" or "close-group" => ("Close box", Glyphs.ShutBox),
        "keep-group" => ("Keep", Glyphs.Keep),
        "ungroup" => ("Ungroup", Glyphs.Ungroup),
        "duplicate-modules" => ("Duplicate", Glyphs.Duplicate),
        "delete-modules" or "delete-group" => ("Delete", Glyphs.Delete),
        "copy-modules" => ("Copy", Glyphs.Copy),
        "cut-modules" => ("Cut", Glyphs.Cut),
        "tidy-selection" => ("Lay out", Glyphs.Tidy),
        _ => null,
    };

    /// <summary>The plate's buttons, row by row: what acts on the block, then what takes the selection elsewhere.</summary>
    public static IReadOnlyList<IReadOnlyList<Button>> Rows(ModulePlate plate) =>
        [.. plate.Under.Children
            .OfType<Panel>()
            .Select(row => (IReadOnlyList<Button>)[.. row.Children.OfType<Button>().Where(b => Of(b.Name) is not null)])
            .Where(row => row.Count > 0)];

    /// <summary>The plate's button called <paramref name="name"/>, if it has one.</summary>
    public static Button? Find(ModulePlate plate, params string[] names) =>
        Rows(plate).SelectMany(row => row).FirstOrDefault(b => names.Contains(b.Name));

    /// <summary>A glyph with its word under it, which is how a finger is shown an action.</summary>
    public static Button Worded(string name, Control glyph, string label, string? tip, double height)
    {
        var button = new Button
        {
            Name = name,
            Height = height,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = Words(glyph, label),
        };

        if (tip is not null) ToolTip.SetTip(button, tip);

        return button;
    }

    /// <summary>What a worded button shows: the glyph, and the word under it.</summary>
    public static Control Words(Control glyph, string label) => new StackPanel
    {
        Spacing = 4,
        HorizontalAlignment = HorizontalAlignment.Center,
        Children =
        {
            new ContentControl { Content = glyph, HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = label, FontSize = Text.Small, HorizontalAlignment = HorizontalAlignment.Center },
        },
    };

    /// <summary>Presses the plate's own button, folded away or not.</summary>
    public static void Press(Button button)
    {
        if (button.IsEnabled) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
