using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core.Graph;
using Flyback.Editor.Bars;
using Flyback.Editor.Canvas;
using Flyback.Editor.Controls;
using Flyback.Editor.Notices;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Inspect;

/// <summary>
/// What the inspector shows for a group: its name, its edge, and what can be
/// done to it.
/// </summary>
/// <remarks>
/// The edge rather than the contents, which is the whole point of the panel: a
/// box is a promise that several modules can be thought about as one thing, and
/// a list of the knobs inside would be the box admitting it never was.
/// </remarks>
/// <param name="palette">The kept groups, and keeping one under its name.</param>
internal sealed class GroupInspector(
    NodeEditor editor,
    StackPanel panel,
    ModuleWash wash,
    ContentControl plateHost,
    Palette palette,
    SocketRows socketRows,
    MeasuredRows measured)
{
    public void Build(NodeGroup group)
    {
        const double socketGutter = 140;

        // The box's own face, in the grays the canvas draws one in — a box belongs to
        // no category, so there is no accent to carry over.
        var plate = ModulePlate.Box();

        wash.ShowBox();
        wash.Off = editor.Edits.SelectionIsOff;

        plate.Named.Children.Add(Title(group, plate.Ink));

        plate.Named.Children.Add(new TextBlock
        {
            Text = group.Name is null ? "Group" : $"Group · {group.Counted}",
            FontSize = Text.Small,
            Foreground = plate.Quiet,
            TextAlignment = TextAlignment.Right,
        });

        plateHost.Content = plate;

        // Under the name, above the description, where a module's own row sits.
        var above = plate.Under.Children.Count;

        panel.Children.Add(new TextBlock
        {
            Text = "Several modules drawn as one. Nothing about the patch changes — the modules "
                 + "are where they were and so are the wires between them.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Text.Muted,
            FontSize = Text.Body,
            Margin = new Thickness(0, 4, 0, 6),
        });

        var sockets = editor.History.Patch.SocketsOf(group);

        // Reserved for every slider on the edge if any one of them has a reading,
        // as a module's panel does.
        var reading = sockets.Inputs.Any(s =>
            editor.History.Patch.Find(s.Node) is { } inner && NodeCatalog.Get(inner.TypeId) is { } def
            && (InspectorRows.Named(def.Inputs[s.Port]) || def.TypeId == NodeCatalog.AutoRemapTypeId));

        Edge("In", sockets.Inputs);
        Edge("Out", sockets.Outputs);

        if (sockets.Rows == 0)
            panel.Children.Add(new TextBlock
            {
                Text = "Nothing has been wired across its edge, so the box has no sockets yet.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Text.Muted,
                FontSize = Text.Body,
            });

        // A box on a locked canvas is drawn from the text's group statements, so
        // everything that would change one is left off for the same reason
        // deleting a module is — see Build.
        if (editor.History.Locked) return;

        var actions = ActionRows.Row();

        // First in the row, as it is on a module's panel: the one action here that
        // changes what the patch does rather than how it is drawn. It switches the
        // modules, since that is all a group is — a box round some of them.
        var switching = editor.Edits.Switchable;

        if (switching > 0)
            Act(
                "switch-group",
                Glyphs.Switch(),
                editor.Edits.SelectionIsOff
                    ? $"Switch the {switching} modules in this box back on  (Ctrl+B)"
                    : $"Switch the {switching} modules in this box off, passing what is patched "
                      + "into it straight through  (Ctrl+B)",
                editor.Edits.SwitchSelected);

        Act(
            group.Collapsed ? "open-group" : "close-group",
            group.Collapsed ? Glyphs.OpenBox() : Glyphs.ShutBox(),
            group.Collapsed
                ? "Open the box, showing the modules in it  (Ctrl+E)"
                : "Close the box, drawing its modules as one  (Ctrl+Shift+E)",
            editor.Edits.ToggleSelectedGroup);

        // Keeping one is not an edit to the patch, so it sits with the one that is
        // not either and ahead of the two that are. What the module list will call
        // it is its name and nothing else — so a group with none is offered the
        // button grayed rather than a button that saves "3 modules" under a heading
        // full of other things called "3 modules". The way out is one gesture up:
        // the title at the top of this panel renames on a double-click.
        var named = !string.IsNullOrWhiteSpace(group.Name);

        // Which of the two things pressing it does, said before it is pressed.
        // Replacing is the one worth knowing about in advance — it is somebody
        // else's group going, and the name is the only warning there is.
        var keep = Act("keep-group", Glyphs.Keep(), !named
            ? "The module list calls a kept group by its name — double-click the title above to give it one."
            : palette.Groups?.Named(group.Name) is not null
                ? $"Replaces the “{group.Name}” already in the module list. It will ask first."
                : $"Keeps “{group.Name}” under Groups in the module list, ready to add again.",
            () => Keep(group, actions));

        keep.IsEnabled = named;

        // The grayed one is precisely the one with something to explain, and a
        // tip that will not show on a disabled control explains it to nobody.
        // The two buttons in the toolbar above that gray themselves out do the
        // same for the same reason.
        ToolTip.SetShowOnDisabled(keep, true);

        Act("ungroup", Glyphs.Ungroup(), "Take the box off, leaving the modules where they are  (Ctrl+Shift+G)", editor.Edits.UngroupSelected);

        Act(
            "delete-group",
            Glyphs.Delete(),
            $"Delete the box and the {group.Members.Count} modules in it  (Delete)",
            editor.Edits.DeleteSelected);

        plate.Under.Children.Insert(above, actions);
        if (ActionRows.Selection(editor) is { } taking) plate.Under.Children.Insert(above + 1, taking);

        // One heading and a row per socket, each named for the module and socket
        // inside that it stands for.
        void Edge(string heading, IReadOnlyList<GroupSocket> sockets)
        {
            if (sockets.Count == 0) return;

            panel.Children.Add(new TextBlock
            {
                Text = heading,
                FontSize = Text.Small,
                FontWeight = FontWeight.SemiBold,
                Opacity = 0.7,
                Margin = new Thickness(0, 10, 0, 2),
            });

            foreach (var socket in sockets)
                if (editor.Selection.Scene.Named(socket) is var (_, spec) && editor.History.Patch.Find(socket.Node) is { } node)
                    panel.Children.Add(Socket(socket, node, spec));
        }

        // The row the module's own panel has for the port, and — on one with nothing plugged into it — the way to
        // take it off the edge again. Only there while it is unwired: a socket a
        // wire is on comes back the moment anything asks, so a button offering
        // to remove one would appear to do nothing.
        Control Socket(GroupSocket socket, NodeInstance node, PortSpec spec)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 1) };

            var name = WireEnds.Name(editor.History.Patch, socket.Node, socket.Port, socket.IsOutput);

            var body = socket.IsOutput
                ? socketRows.Output(node, name, socket.Port)
                : socketRows.Input(NodeCatalog.Get(node.TypeId)!, node, spec, socket.Port, reading, name);

            // A module's gutter fits a socket's name, and this caption is a module's as well.
            if (body is Grid grid && grid.Children.OfType<TextBlock>().FirstOrDefault() is { } caption)
            {
                grid.ColumnDefinitions[0].Width = new GridLength(socketGutter);
                if (!double.IsNaN(caption.Width)) caption.Width = socketGutter;

                caption.Foreground = new SolidColorBrush(Colors.PortColor(spec.Kind));
                ToolTip.SetTip(caption, spec.Help.Length == 0 ? name : $"{name}: {spec.Help}");
            }

            if (!editor.History.Patch.Wired(group, socket) && !editor.History.Locked)
            {
                var remove = new Button
                {
                    Name = "hideSocket",
                    Content = Glyphs.Cross(12),
                    FontSize = Text.Caption,
                    Padding = new Thickness(5, 0, 5, 0),
                    Background = Brushes.Transparent,
                    Opacity = 0.55,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                ToolTip.SetTip(remove, "Take this socket off the edge. A wire puts it back.");
                remove.Click += (_, _) => editor.Edits.HideSocket(group, socket);

                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
            }

            row.Children.Add(body);

            var helped = InspectorRows.Helped(row, spec.Help);

            if (!socket.IsOutput || measured.Under(node, socket.Port).ToList() is not { Count: > 0 } under) return helped;

            var stack = new StackPanel { Children = { helped } };
            stack.Children.AddRange(under);
            return stack;
        }

        Button Act(string name, Control icon, string tip, Action gesture)
        {
            var button = ToolbarButtons.Drawn(name, icon, tip);

            button.Click += (_, _) => gesture();
            actions.Children.Add(button);

            return button;
        }
    }

    /// <summary>
    /// Keeps a group in the module list, asking first where doing so would replace
    /// one already kept under that name.
    /// </summary>
    /// <remarks>
    /// Replacing is somebody's group going for good, with nothing on this side of
    /// it to undo, and a name typed a second time by accident is the ordinary way
    /// to lose one. Asked in the place the button was standing — the row of them
    /// gives way to the question — the way the module list asks about a row that is
    /// going: a dialog would be right if this could lose work, and what it can lose
    /// is one entry in a list.
    /// </remarks>
    private void Keep(NodeGroup group, StackPanel actions)
    {
        if (palette.Groups is not { } kept || string.IsNullOrWhiteSpace(group.Name)) return;

        var host = actions.Parent as Panel;
        var at = host?.Children.IndexOf(actions) ?? -1;

        // Nothing kept under that name, or no row left to ask in — either way
        // there is nothing to ask about.
        if (kept.Named(group.Name) is null || host is null || at < 0)
        {
            palette.SaveGroup(group);
            return;
        }

        host.Children[at] = Question.Row(
            $"Replace “{group.Name}”?",
            actions.Margin,
            $"Replace the kept “{group.Name}” with this group.",
            "Leave the kept one alone.",
            replace =>
            {
                if (replace) palette.SaveGroup(group);

                // Put back exactly what a fresh panel would have, which is the
                // button saying whatever it should say now — a replaced group is
                // one this list already knows, so its tip changes.
                editor.Reactions.Raise(new PanelStale());
            });
    }

    private Control Title(NodeGroup group, IBrush ink)
    {
        var title = new TextBlock
        {
            Text = group.Title(),
            FontSize = Text.Title,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Right,
            Foreground = ink,
            Background = Brushes.Transparent,

            // Struck through while every module in the box is off — see
            // ADR-0117, and BuildTitle's own strike for a single module.
            TextDecorations = editor.Edits.SelectionIsOff ? TextDecorations.Strikethrough : null,
        };

        ToolTip.SetTip(title, group.Name is null
            ? "Double-click to give this group a name of its own."
            : $"Double-click to rename. Empty the box to go back to '{group.Counted}'.");

        title.Cursor = NameBox.Renaming;

        title.DoubleTapped += (_, e) =>
        {
            e.Handled = true;

            NameBox.Open(
                title,
                ink,
                group.Name,
                group.Counted,
                NodeGroup.NameLimit,
                typed => group.Rename(typed),
                () => group.Name,
                () => Title(group, ink),
                () => editor.History.Record());
        };

        return title;
    }
}
