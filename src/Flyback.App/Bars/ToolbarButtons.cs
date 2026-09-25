using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Controls;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Bars;

/// <summary>The toolbar's buttons, its groups and the lines between them.</summary>
internal static class ToolbarButtons
{
    /// <summary>One group of toolbar controls, laid out along it.</summary>
    internal static StackPanel Group() => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Margin = new Thickness(12, 8),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// A toolbar button that is a symbol rather than a word.
    /// </summary>
    /// <remarks>
    /// With the labels gone the tip is the only place the button says what it does,
    /// so every one has one and it is a sentence rather than a repeat of the icon's
    /// name. Named as well, so a test can find the button without reading a glyph.
    /// </remarks>
    internal static Button Glyph(string name, string glyph, string tip) =>
        Marked(new Button(), name, glyph, tip);

    /// <summary>The same, for the two icons that are drawn rather than typed.</summary>
    internal static Button Drawn(string name, Control icon, string tip) =>
        Marked(new Button(), name, icon, tip);

    /// <summary>The same, for a button that stays down.</summary>
    internal static ToggleButton Toggle(string name, string glyph, string tip) =>
        Marked(new ToggleButton(), name, glyph, tip);

    internal static T Marked<T>(T button, string name, object content, string tip)
        where T : ContentControl
    {
        button.Name = name;
        button.Content = content;
        button.Width = 34;
        button.Height = 30;
        button.Padding = new Thickness(0);
        button.FontSize = Text.Heading;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;

        ToolTip.SetTip(button, tip);

        return button;
    }

    internal static Control Separator() => new Border
    {
        Width = 1,
        Margin = new Thickness(4, 4),
        Background = new SolidColorBrush(Colors.Separator),
    };
}
