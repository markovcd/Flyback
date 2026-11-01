using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Canvas;
using Flyback.Editor.Hubs;
using Flyback.Engine.Language;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Inspect;

/// <summary>
/// The levels the selected module sits in, outermost first, as <c>Bass › bass = voice › shaped</c>:
/// each a button that selects every module in that level.
/// </summary>
internal static class LevelPath
{
    public const string Name = "level-path";

    /// <summary>The path to <paramref name="module"/>, or null for one no level placed.</summary>
    /// <param name="fingers">Whether a finger is in use, which wants each button 44 tall.</param>
    public static WrapPanel? Of(NodeEditor editor, PatchLevels levels, Guid module, bool fingers)
    {
        var path = levels.PathTo(module);

        if (path.Count == 0) return null;

        var row = new WrapPanel
        {
            Name = Name,
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6),
        };

        for (var i = 0; i < path.Count; i++)
        {
            if (i > 0)
                row.Children.Add(new TextBlock
                {
                    Text = "›",
                    Foreground = Text.Muted,
                    FontSize = Text.Small,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0),
                });

            row.Children.Add(Step(editor, levels, path[i], i, module, fingers));
        }

        return row;
    }

    private static Button Step(NodeEditor editor, PatchLevels levels, Level level, int depth, Guid module, bool fingers)
    {
        var members = levels.Present(level);
        var chosen = members.Count > 1 && editor.Selection.Count == members.Count && members.All(editor.Selection.Contains);

        var button = new Button
        {
            Name = $"level-{depth}",
            Content = level.Item is { } item && level.Kind == LevelKind.Pass ? $"{level.Label}  {item}" : level.Label,
            FontSize = Text.Small,
            FontWeight = chosen ? FontWeight.SemiBold : FontWeight.Normal,
            Padding = new Thickness(6, 0),
            MinHeight = fingers ? 44 : 24,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = chosen ? null : Brushes.Transparent,
            Tag = level,
        };

        ToolTip.SetTip(button, $"Select the {members.Count} module(s) in this level, from line {level.Line}");

        button.Click += (_, _) =>
        {
            editor.Selection.Take(members, module);
            editor.Selection.Announce();
        };

        return button;
    }
}
