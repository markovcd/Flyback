using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Flyback.Core.Graph;
using Flyback.Editor.Bars;
using Flyback.Editor.Canvas;
using Flyback.Editor.Controls;
using Flyback.Editor.Notices;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Inspect;

/// <summary>The rows of glyph buttons under the name at the top of the inspector (ADR-0111).</summary>
internal static class ActionRows
{
    /// <summary>
    /// The strip of buttons under the name at the top of the panel: what can be
    /// done to what is selected, each a glyph with the sentence in its tip.
    /// </summary>
    /// <remarks>
    /// A row rather than a column, because a glyph is the width of a button and a
    /// column of them would leave the panel empty beside it. The tip is the only
    /// place a button without words can say what it does, so every one has one and
    /// it carries the count where there is one to carry.
    /// </remarks>
    public static StackPanel Row() => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Margin = new Thickness(0, 2, 0, 6),

        // The right, where the name and the category are: everything the plate
        // carries is read down the one edge.
        HorizontalAlignment = HorizontalAlignment.Right,
    };

    /// <summary>
    /// What the selection can be taken somewhere with: the clipboard's copy and cut, and
    /// laying out the selection alone. Each is a key or a held Ctrl for a hand that has one.
    /// </summary>
    public static StackPanel? Selection(NodeEditor editor)
    {
        var row = Row();
        row.Margin = new Thickness(0, 0, 0, 6);

        var copying = editor.Selection.Nodes.Count(n => !NodeCatalog.IsSink(n.TypeId));
        var these = copying > 1 ? $"these {copying} modules" : "this module";

        if (copying > 0)
        {
            Act("copy-modules", Glyphs.Copy(), $"Copy {these} to the clipboard  (Ctrl+C)", editor.Copy);
            Act("cut-modules", Glyphs.Cut(), $"Cut {these}, leaving them on the clipboard  (Ctrl+X)", editor.Cut);
        }

        if (editor.Selection.Count > 1)
            Act(
                "tidy-selection",
                Glyphs.Tidy(),
                $"Lay out only these {editor.Selection.Count} modules, leaving the rest where they are  (Ctrl+Shift+L)",
                () => editor.Reactions.Raise(new TidyAsked(OnlySelected: true)));

        return row.Children.Count == 0 ? null : row;

        void Act(string name, Control icon, string tip, Action gesture)
        {
            var button = ToolbarButtons.Drawn(name, icon, tip);

            button.Click += (_, _) => gesture();
            row.Children.Add(button);
        }
    }
}
