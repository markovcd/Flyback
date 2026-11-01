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

        return path.Count == 0 ? null : Row(editor, levels, path, module, fingers);
    }

    /// <summary>The levels of <paramref name="path"/> as buttons, outermost first.</summary>
    /// <param name="focus">The module the inspector stays on after a press, or null for the first in the level.</param>
    public static WrapPanel Row(NodeEditor editor, PatchLevels levels, IReadOnlyList<Level> path, Guid? focus, bool fingers)
    {
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

            row.Children.Add(Button(editor, levels, path[i], $"level-{i}", focus, fingers));
        }

        return row;
    }

    /// <summary>A button naming <paramref name="level"/> that selects every module in it, in bold while they are the selection.</summary>
    /// <param name="label">What it reads, where not the level's own label.</param>
    public static Button Button(NodeEditor editor, PatchLevels levels, Level level, string name, Guid? focus, bool fingers, string? label = null)
    {
        var members = levels.Present(level);
        var chosen = members.Count > 1 && editor.Selection.Count == members.Count && members.All(editor.Selection.Contains);

        var button = new Button
        {
            Name = name,
            Content = label ?? (level.Item is { } item && level.Kind == LevelKind.Pass ? $"{level.Label}  {item}" : level.Label),
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
            editor.Selection.Take(members, focus is { } kept && members.Contains(kept) ? kept : null);
            editor.Selection.Announce();
        };

        return button;
    }
}
