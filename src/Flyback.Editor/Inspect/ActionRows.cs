using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Flyback.Core.Graph;
using Flyback.Editor.Bars;
using Flyback.Editor.Canvas;
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
    /// What can be done to the selected module: switched, grouped, its boxes
    /// opened or closed, duplicated and deleted, in that order so the
    /// destructive button is at the far end of the row.
    /// </summary>
    public static StackPanel Module(NodeEditor editor)
    {
        var actions = Row();

        // First in the row, because it is the one action here that changes what
        // the patch does rather than how it is drawn. It counts the selection the
        // way delete does, and leaves the Output out of the count for the same
        // reason: that one is never switched off.
        var switching = editor.Edits.Switchable;

        if (switching > 0)
        {
            var back = editor.Edits.SelectionIsOff;

            Act(
                "switch-modules",
                Glyphs.Switch(),
                (switching > 1, back) switch
                {
                    (true, true) => $"Switch these {switching} modules back on  (Ctrl+B)",
                    (true, false) => $"Switch these {switching} modules off  (Ctrl+B)",
                    (false, true) => "Switch this module back on  (Ctrl+B)",
                    (false, false) =>
                        "Switch this module off, passing what is patched into it straight through  (Ctrl+B)",
                },
                editor.Edits.SwitchSelected);
        }

        // Grouping comes ahead of deleting, so the destructive button is at the far
        // end of the row rather than the first thing under the pointer.
        //
        // Its tip counts the way delete's does — see NodeEditor.Groupable — and it
        // is offered on the same terms Ctrl+G is: a button offering to group one
        // module would offer something the graph refuses. Ungrouping is not here,
        // because a selection that is exactly a group gets a panel of its own.
        if (editor.Edits.Groupable >= NodeGroup.Fewest)
            Act("group", Glyphs.Group(), $"Draw these {editor.Edits.Groupable} modules as one box  (Ctrl+G)", editor.Edits.GroupSelected);

        // For a selection that reaches into groups without being one: the group
        // panel above answers only a selection that is exactly one, and a
        // double-click only the box it lands on.
        var shut = editor.Selection.Groups.Count(g => g.Collapsed);
        var open = editor.Selection.Groups.Count(g => !g.Collapsed);

        if (shut > 0)
            Act(
                "open-groups",
                Glyphs.OpenBox(),
                shut > 1
                    ? $"Open the {shut} boxes the selection touches  (Ctrl+E)"
                    : "Open the box, showing the modules in it  (Ctrl+E)",
                editor.Edits.OpenSelectedGroups);

        if (open > 0)
            Act(
                "close-groups",
                Glyphs.ShutBox(),
                open > 1
                    ? $"Close the {open} boxes the selection touches  (Ctrl+Shift+E)"
                    : "Close the box, drawing its modules as one  (Ctrl+Shift+E)",
                editor.Edits.CloseSelectedGroups);

        // Delete takes the whole selection, the same as the key does, so the tip
        // counts it. Sinks are left out of the count because the graph refuses
        // them: a button offering to delete three when it can only manage two
        // would be lying about what pressing it does.
        var going = editor.Selection.Nodes.Count(n => !NodeCatalog.IsSink(n.TypeId));

        if (going > 0)
            Act(
                "duplicate-modules",
                Glyphs.Duplicate(),
                going > 1 ? $"Duplicate these {going} modules  (Ctrl+D)" : "Duplicate this module  (Ctrl+D)",
                editor.Edits.DuplicateSelection);

        Act(
            "delete-modules",
            Glyphs.Delete(),
            going > 1 ? $"Delete these {going} modules  (Delete)" : "Delete this module  (Delete)",
            editor.Edits.DeleteSelected);

        return actions;

        void Act(string name, Control icon, string tip, Action gesture)
        {
            var button = ToolbarButtons.Drawn(name, icon, tip);

            button.Click += (_, _) => gesture();
            actions.Children.Add(button);
        }
    }

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
