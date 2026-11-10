using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Controls;

/// <summary>The toolbar's buttons, its groups and the lines between them.</summary>
internal static class ToolbarButtons
{
    /// <summary>One group of toolbar controls, laid out along it.</summary>
    internal static WrapPanel Group() => new()
    {
        Orientation = Orientation.Horizontal,
        ItemSpacing = 8,
        LineSpacing = 8,
        Margin = new Thickness(12, 8),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The style class that takes a button's fill away at rest.</summary>
    internal const string Tool = "tool";

    /// <summary>
    /// A toolbar button that is a drawn symbol rather than a word.
    /// </summary>
    /// <remarks>
    /// With the labels gone the tip is the only place the button says what it does,
    /// so every one has one and it is a sentence rather than a repeat of the icon's
    /// name. Named as well, so a test can find the button without reading a glyph.
    /// </remarks>
    /// <param name="legend">The word printed under the symbol while the toolbar shows its labels.</param>
    internal static Button Drawn(string name, Control icon, string tip, string? legend = null) =>
        Marked(new Button(), name, Labeled(icon, legend), tip);

    /// <summary>The same, for a button that stays down.</summary>
    internal static ToggleButton Toggle(string name, Control icon, string tip, string? legend = null) =>
        Marked(new ToggleButton(), name, Labeled(icon, legend), tip);

    /// <summary>The symbol with its legend under it, the legend hidden until <see cref="ShowLegend"/> says otherwise.</summary>
    private static Control Labeled(Control icon, string? legend) => legend is null
        ? icon
        : new StackPanel
        {
            Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                icon,
                new TextBlock
                {
                    Text = legend,
                    FontSize = LegendSize,
                    FontWeight = FontWeight.Bold,
                    LetterSpacing = 0.8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    IsVisible = false,
                },
            },
        };

    /// <summary>How large a legend is printed.</summary>
    private const double LegendSize = 8;

    /// <summary>Shows or hides a button's legend, sizing the button for it. A button with no legend is left alone.</summary>
    internal static void ShowLegend(ContentControl button, bool shown)
    {
        if (button.Content is not StackPanel { Children: [_, TextBlock legend] }) return;

        legend.IsVisible = shown;
        button.Width = shown ? 48 : 34;
        button.Height = shown ? 42 : 30;
    }

    /// <summary>The legend a button prints, or null for one that has none; only tests call it.</summary>
    internal static string? LegendOf(ContentControl button) =>
        button.Content is StackPanel { Children: [_, TextBlock legend] } ? legend.Text : null;

    /// <summary>Changes a button's symbol, keeping its legend.</summary>
    internal static void SetIcon(ContentControl button, Control icon)
    {
        if (button.Content is StackPanel { Children: [_, TextBlock] } labeled) labeled.Children[0] = icon;
        else button.Content = icon;
    }

    internal static T Marked<T>(T button, string name, object content, string tip)
        where T : ContentControl
    {
        button.Name = name;
        button.Classes.Add(Tool);
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
