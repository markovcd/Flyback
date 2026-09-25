using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.App.Midi;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App;

/// <summary>
/// The panel on the right: the selected module's knobs, or the group's, or what
/// the patch says about itself when nothing is selected (ADR-0148).
/// </summary>
/// <remarks>
/// Rebuilt from nothing every time the selection changes, because what it shows
/// is entirely the selected module's port list.
/// </remarks>
internal sealed class Inspector
{
    private readonly IFilePickers pickers;
    /// <summary>
    /// How far the panel's rows keep off its edges. Named because the plate at the
    /// head of it takes the inset back off again to reach them.
    /// </summary>
    internal const double PanelInset = 12;

    private readonly NodeEditor editor;
    private readonly Document document;
    private readonly MidiHub midi;
    private readonly InstrumentLibrary instruments;
    private readonly SampleLibrary soundFolder;
    private readonly ImageLibrary pictureFolder;
    private readonly Func<GroupLibrary?> groups;
    private readonly Action<NodeGroup> saveGroup;

    /// <summary>
    /// Named so a test can find it. It is the one panel here that is switched
    /// off whole while the text owns the patch, and there is nothing else about it
    /// to tell it apart by.
    /// </summary>
    private readonly StackPanel panel = new()
    {
        Name = "inspector",
        Margin = new Thickness(PanelInset),
        Spacing = 8,
    };

    /// <summary>
    /// The selected block's background, behind everything on the panel and fading
    /// out down it, with the block's mark set large in it.
    /// </summary>
    private readonly ModuleWash wash = new();

    /// <summary>
    /// Where the plate stands: above the scroller rather than in it, so the name and
    /// the buttons are there at every scroll position.
    /// </summary>
    private readonly ContentControl plateHost = new() { Name = "plate-host" };

    /// <param name="knobs">The instruments a MIDI In can be played from.</param>
    /// <param name="files">The folders the patch reads its sound files and pictures from.</param>
    /// <param name="palette">The kept groups, and keeping one under its name.</param>
    public Inspector(NodeEditor editor, Document document, MidiHub midi, PanelKnobs knobs, PatchFiles files, Palette palette, IFilePickers pickers)
    {
        this.pickers = pickers;
        this.editor = editor;
        this.document = document;
        this.midi = midi;
        instruments = knobs.Instruments;
        soundFolder = files.SoundFolder;
        pictureFolder = files.PictureFolder;
        groups = () => palette.Groups;
        saveGroup = palette.SaveGroup;
    }

    /// <summary>The rows, which scroll.</summary>
    public StackPanel Panel => panel;

    /// <summary>The block's face, behind the whole column.</summary>
    public ModuleWash Wash => wash;

    /// <summary>The plate, docked above the rows.</summary>
    public ContentControl PlateHost => plateHost;

    /// <summary>
    /// What the panel's rows are, as against what is in them: which module is being
    /// shown, and which of its inputs have a wire on them.
    /// </summary>
    /// <remarks>
    /// A row is a knob or the word "patched" and never both, so a wire landing on
    /// the selected module changes the panel as much as selecting another does.
    /// Nothing else the canvas reports does, which is what keeps a slider mid-drag
    /// from being torn down under the hand holding it.
    /// </remarks>
    private string inspectorShape = string.Empty;

    /// <summary>
    /// Rebuilds the panel if a wire has arrived at or left the module it is
    /// showing. Hung off every patch change, because patching is not a selection
    /// change and the panel has no other way to hear about it.
    /// </summary>
    public void Sync()
    {
        if (InspectorShape.Of(editor) != inspectorShape) Build();
    }

    /// <summary>
    /// Rebuilt whenever the selection changes, and whenever a wire changes what
    /// the selected module's rows are. The canvas handles patching; exact
    /// numbers are easier to set with real controls than by dragging on a knob.
    /// </summary>
    public void Build()
    {
        inspectorShape = InspectorShape.Of(editor);
        panel.Children.Clear();
        plateHost.Content = null;

        // What an empty panel says depends on which canvas is under it. Naming
        // gestures that are switched off would be worse than saying nothing: a
        // person following them would conclude the program was broken rather
        // than that the patch belongs to the text — see ADR-0068.
        if (editor.Selection.Focused is not { } node || NodeCatalog.Get(node.TypeId) is not { } def)
        {
            wash.Clear();
            plateHost.Content = null;

            panel.Children.Add(BuildPatchDescription());
            panel.Children.Add(BuildPatchAuthor());
            panel.Children.Add(BuildPatchTags());

            panel.Children.Add(new TextBlock
            {
                Text = document.IsAdrift
                    ? document.IsAdriftBox ? InspectorHelp.AdriftingGroup : InspectorHelp.Adrifting
                    : editor.History.Locked ? InspectorHelp.Locked : InspectorHelp.Canvas,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Text.Muted,
                FontSize = Text.Body,
            });
            return;
        }

        // A selection that is exactly a group is about the group, not about
        // whichever of its modules the pointer last came down on. Ahead of
        // everything below, because none of it applies: a box has no knobs, no
        // description and no category — what it has is an edge.
        if (editor.Selection.Group is { } group)
        {
            BuildGroupInspector(group);
            return;
        }

        // The block's own face, so the panel and the canvas are plainly the same
        // module. Its name and what can be done to it stand on the plate; the rows
        // below stay on the panel, which is what a column of numbers is read on.
        var plate = ModulePlate.Of(def);

        wash.Show(def);
        wash.Off = node.Off;

        plate.Named.Children.Add(BuildTitle(node, def, plate.Ink));

        plate.Named.Children.Add(new TextBlock
        {
            Text = def.Category,
            FontSize = Text.Small,
            Foreground = plate.Quiet,
            TextAlignment = TextAlignment.Right,
        });

        plateHost.Content = plate;

        // What can be done to the module goes under its name, above the
        // description — see ActionRow. Where it goes is settled here and what is
        // in it at the end, because a knob does not decide whether a module can
        // be grouped.
        var above = plate.Under.Children.Count;

        if (!string.IsNullOrEmpty(def.Description))
            panel.Children.Add(new TextBlock
            {
                Text = def.Description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ModulePlate.BodyQuiet(def),
                FontSize = Text.Body,
                Margin = new Thickness(0, 4, 0, 6),
            });

        if (BuildNormalledNote(node, def) is { } normalled) panel.Children.Add(normalled);

        // Checked once for the whole panel rather than row by row, so that a
        // bar is the same width down the entire module: a module where nothing
        // has a reading gives every slider the column back, and a module where
        // even one socket does reserves it for all of them, named or not.
        var reading = InspectorRows.ShowsReading(def) || def.TypeId == NodeCatalog.AutoRemapTypeId;

        for (var i = 0; i < def.Inputs.Count; i++)
            panel.Children.Add(Edged(Helped(BuildInputRow(def, node, def.Inputs[i], i, reading), def.Inputs[i].Help), node, i, output: false));

        // Whatever the module carries that is not a knob, each kind edited by the
        // control that suits it. This mapping lives here rather than on the extra
        // because it is the one part of a kind that needs Avalonia, which the
        // engine does not reference.
        foreach (var extra in def.Extras)
            if (EditorFor(extra, node, def, reading) is { } control)
                panel.Children.Add(extra.Fields.Count == 0 ? Helped(control, extra.Help) : control);

        if (BuildKeyboardSection(node, def) is { } keyboard) panel.Children.Add(keyboard);

        if (def.Inputs.Count == 0 && def.Extras.Count == 0)
            panel.Children.Add(new TextBlock
            {
                Text = "This module has nothing to set — it only produces.",
                Foreground = Text.Muted,
                FontSize = Text.Body,
            });

        BuildOutputs(def, node);

        // The Output cannot be deleted, so it gets no button for it. What the
        // picture is drawn at and by is a property of the machine rather than of
        // this block, and is in the settings window — ADR-0082.
        if (NodeCatalog.IsSink(node.TypeId))
        {
            Undescribed();
            return;
        }

        // What the graph is made of belongs to whoever owns it. A knob turned on
        // a locked canvas is written back into the text (ADR-0068); a module
        // deleted from one could not be, so the button is not offered rather
        // than offered and undone by the next apply.
        if (editor.History.Locked)
        {
            Undescribed();
            return;
        }

        var actions = ActionRow();

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

        Act(
            "delete-modules",
            Glyphs.Delete(),
            going > 1 ? $"Delete these {going} modules  (Delete)" : "Delete this module  (Delete)",
            editor.Edits.DeleteSelected);

        plate.Under.Children.Insert(above, actions);

        Undescribed();

        // Last, under everything the module has: it is a note about the
        // assistant, and the one thing on the panel not about the patch.
        void Undescribed()
        {
            if (!editor.Tags.Types.Contains(def.TypeId)) return;

            panel.Children.Add(new TextBlock
            {
                Name = "undescribedNote",
                Text = AssistantPanel.UndescribedNote,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Text.Muted,
                FontSize = Text.Small,
                Margin = new Thickness(0, 18, 0, 0),
            });
        }

        void Act(string name, Control icon, string tip, Action gesture)
        {
            var button = ToolbarButtons.Drawn(name, icon, tip);

            button.Click += (_, _) => gesture();
            actions.Children.Add(button);
        }
    }

    /// <summary>
    /// What the module puts out, under everything that can be set: a heading and a
    /// row per output, each with its help as the tip and what it feeds beside it.
    /// </summary>
    private void BuildOutputs(NodeDef def, NodeInstance node)
    {
        if (def.Outputs.Count == 0) return;

        panel.Children.Add(new TextBlock
        {
            Text = "Outputs",
            FontSize = Text.Small,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.7,
            Margin = new Thickness(0, 10, 0, 2),
        });

        for (var i = 0; i < def.Outputs.Count; i++)
            panel.Children.Add(Edged(Helped(BuildOutputRow(node, def.Outputs[i].Name, i), def.Outputs[i].Help), node, i, output: true));
    }

    /// <summary>
    /// A grouped module's unwired socket row, with the button that puts it on the
    /// box's edge: the counterpart of the ✕ on the group's panel.
    /// </summary>
    private Control Edged(Control row, NodeInstance node, int port, bool output)
    {
        var socket = new GroupSocket(node.Id, port, output);

        if (editor.History.Locked
            || editor.History.Patch.GroupOf(node.Id) is not { } group
            || !editor.History.Patch.Exposable(group, socket))
            return row;

        var expose = new Button
        {
            Name = "exposeSocket",
            Content = output ? "⇥" : "⇤",
            FontSize = Text.Caption,
            Padding = new Thickness(5, 0, 5, 0),
            Background = Brushes.Transparent,
            Opacity = 0.55,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(expose, $"Put this socket on the edge of “{group.Title()}”.");
        expose.Click += (_, _) => editor.Edits.ExposeSocket(group, socket);

        var edged = new DockPanel();

        DockPanel.SetDock(expose, Dock.Right);
        edged.Children.Add(expose);
        edged.Children.Add(row);

        return edged;
    }

    /// <summary>An output's name, and what it feeds beside it.</summary>
    private Grid BuildOutputRow(NodeInstance node, string name, int index)
    {
        var row = InspectorRows.Row("*");
        var caption = InspectorRows.Caption(name);
        var feeds = WireEnds.OutOf(editor.History.Patch, node.Id, index);

        caption.Margin = new Thickness(0, 2, 0, 2);
        row.Children.Add(caption);

        if (feeds is null)
        {
            caption.Width = double.NaN;
            Grid.SetColumnSpan(caption, 2);
        }
        else
        {
            var wired = new TextBlock
            {
                Text = feeds,
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            Grid.SetColumn(wired, 1);
            row.Children.Add(wired);
        }

        return row;
    }

    /// <summary>
    /// A socket's or a setting's row with its help as its tip, over the whole row
    /// rather than the name alone. A row with no help is handed back as it was.
    /// </summary>
    private static Control Helped(Control row, string help)
    {
        if (help.Length == 0) return row;

        // A panel with no background is only hit where its children are, which
        // would leave the gaps between them without the tip.
        if (row is Panel { Background: null } panel) panel.Background = Brushes.Transparent;

        ToolTip.SetTip(row, help);

        return row;
    }

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
    private static StackPanel ActionRow() => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Margin = new Thickness(0, 2, 0, 6),

        // The right, where the name and the category are: everything the plate
        // carries is read down the one edge.
        HorizontalAlignment = HorizontalAlignment.Right,
    };

    /// <summary>
    /// What a group shows: its name, its edge, and what can be done to it.
    /// </summary>
    /// <remarks>
    /// The edge rather than the contents, which is the whole point of the panel: a
    /// box is a promise that several modules can be thought about as one thing, and
    /// a list of the knobs inside would be the box admitting it never was.
    /// </remarks>
    private void BuildGroupInspector(NodeGroup group)
    {
        const double SocketGutter = 140;

        // The box's own face, in the grays the canvas draws one in — a box belongs to
        // no category, so there is no accent to carry over.
        var plate = ModulePlate.Box();

        wash.ShowBox();
        wash.Off = editor.Edits.SelectionIsOff;

        plate.Named.Children.Add(BuildGroupTitle(group, plate.Ink));

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

        var actions = ActionRow();

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
            : groups()?.Named(group.Name) is not null
                ? $"Replaces the “{group.Name}” already in the module list. It will ask first."
                : $"Keeps “{group.Name}” under Groups in the module list, ready to add again.",
            () => KeepGroup(group, actions));

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

            // The inner socket itself, not the box's label for it: an Expression's
            // is named for what feeds it, which the row already says.
            var name = WireEnds.Name(editor.History.Patch, socket.Node, socket.Port, socket.IsOutput);

            var body = socket.IsOutput
                ? BuildOutputRow(node, name, socket.Port)
                : BuildInputRow(NodeCatalog.Get(node.TypeId)!, node, spec, socket.Port, reading, name);

            // A module's gutter fits a socket's name, and this caption is a module's as well.
            if (body is Grid grid && grid.Children.OfType<TextBlock>().FirstOrDefault() is { } caption)
            {
                grid.ColumnDefinitions[0].Width = new GridLength(SocketGutter);
                if (!double.IsNaN(caption.Width)) caption.Width = SocketGutter;

                caption.Foreground = new SolidColorBrush(Colors.PortColor(spec.Kind));
                ToolTip.SetTip(caption, spec.Help.Length == 0 ? name : $"{name}: {spec.Help}");
            }

            if (!editor.History.Patch.Wired(group, socket) && !editor.History.Locked)
            {
                var remove = new Button
                {
                    Content = "✕",
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
            return Helped(row, spec.Help);
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
    private void KeepGroup(NodeGroup group, StackPanel actions)
    {
        if (groups() is not { } kept || string.IsNullOrWhiteSpace(group.Name)) return;

        var host = actions.Parent as Panel;
        var at = host?.Children.IndexOf(actions) ?? -1;

        // Nothing kept under that name, or no row left to ask in — either way
        // there is nothing to ask about.
        if (kept.Named(group.Name) is null || host is null || at < 0)
        {
            saveGroup(group);
            return;
        }

        host.Children[at] = Question.Row(
            $"Replace “{group.Name}”?",
            actions.Margin,
            $"Replace the kept “{group.Name}” with this group.",
            "Leave the kept one alone.",
            replace =>
            {
                if (replace) saveGroup(group);

                // Put back exactly what a fresh panel would have, which is the
                // button saying whatever it should say now — a replaced group is
                // one this list already knows, so its tip changes.
                Build();
            });
    }

    private Control BuildGroupTitle(NodeGroup group, IBrush ink)
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
                () => BuildGroupTitle(group, ink),
                () => editor.History.Record());
        };

        return title;
    }

    /// <summary>
    /// What the patch is for, at the top of an empty panel, which a double-click
    /// turns into a box to write it in.
    /// </summary>
    /// <remarks>
    /// Editable on a locked canvas too: it is written back into the text, as the
    /// keyboard's layout is. Not while the text has moved on from the patch, when
    /// the line it would land on may not be the one playing.
    /// </remarks>
    private Control BuildPatchDescription() => BuildPatchLine(
        "patch-description",
        editor.History.Patch.Description,
        editor.History.Patch.Description,
        "Double-click to say what this patch is for.",
        "What is this patch for?",
        Patch.DescriptionLimit,
        typed => editor.History.Patch.Describe(typed),
        () => editor.History.Patch.Description,
        BuildPatchDescription,
        new Thickness(0, 0, 0, 6));

    /// <summary>Who made the patch, under its description, edited the same way.</summary>
    private Control BuildPatchAuthor() => BuildPatchLine(
        "patch-author",
        editor.History.Patch.Author is { } author ? "by " + author : null,
        editor.History.Patch.Author,
        "Double-click to say who made it.",
        "Who made this patch?",
        Patch.AuthorLimit,
        typed => editor.History.Patch.Credit(typed),
        () => editor.History.Patch.Author,
        BuildPatchAuthor,
        new Thickness(0, 0, 0, 6));

    /// <summary>
    /// The patch's tags, under its author, edited as one line of words apart by
    /// spaces or commas.
    /// </summary>
    private Control BuildPatchTags() => BuildPatchLine(
        "patch-tags",
        editor.History.Patch.Tags is { } tags ? string.Join(", ", tags) : null,
        editor.History.Patch.Tags is { } held ? string.Join(' ', held) : null,
        "Double-click to tag it.",
        "drone slow ambient",
        Patch.TagCount * (Patch.TagLimit + 2),
        typed => editor.History.Patch.Tag(typed?.Replace(',', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
        () => editor.History.Patch.Tags is { } now ? string.Join(' ', now) : null,
        BuildPatchTags,
        new Thickness(0, 0, 0, 14));

    /// <summary>
    /// One thing said about the whole patch, which a double-click turns into a box
    /// to write it in.
    /// </summary>
    /// <param name="shown">What the panel shows, and null where nothing is said.</param>
    /// <param name="held">What the box opens holding.</param>
    /// <param name="asking">What the panel shows in its place where nothing is said.</param>
    private Control BuildPatchLine(
        string name,
        string? shown,
        string? held,
        string asking,
        string fallback,
        int limit,
        Action<string?> set,
        Func<string?> current,
        Func<Control> rebuild,
        Thickness margin)
    {
        var ink = new SolidColorBrush(Colors.Label);

        var line = new TextBlock
        {
            Name = name,
            Text = shown ?? asking,
            TextWrapping = TextWrapping.Wrap,
            FontSize = Text.Body,
            FontStyle = shown is null ? FontStyle.Italic : FontStyle.Normal,
            Foreground = shown is null ? Text.Muted : ink,
            Background = Brushes.Transparent,
            Margin = margin,
        };

        if (document.IsAdrift)
        {
            line.IsVisible = shown is not null;
            return line;
        }

        if (shown is not null) ToolTip.SetTip(line, "Double-click to change it. Empty the box to take it away.");

        line.Cursor = NameBox.Renaming;

        line.DoubleTapped += (_, e) =>
        {
            e.Handled = true;

            NameBox.Open(
                line,
                ink,
                held,
                fallback,
                limit,
                set,
                current,
                rebuild,
                () =>
                {
                    // Finished as it closes: Enter takes the box away before any
                    // key comes up in the panel to say so.
                    document.Relaid();
                    editor.History.Record();
                    document.HandCameOff();
                },
                prose: true);
        };

        return line;
    }

    /// <summary>
    /// The name at the top of the panel, which a double-click turns into a box to
    /// type another one into.
    /// </summary>
    /// <remarks>
    /// A module is a thing on a canvas before it is a type, and a patch with four
    /// Mixers in it is one you have to follow a wire to read. The name is only ever
    /// a label: nothing is found by it. Transparent rather than unpainted, because
    /// a <see cref="TextBlock"/> with no background is not there as far as the
    /// pointer is concerned.
    /// </remarks>
    private Control BuildTitle(NodeInstance node, NodeDef def, IBrush ink)
    {
        var title = new TextBlock
        {
            Name = "moduleName",

            // The same heading the canvas draws — a Send or a Receive names its
            // bus alongside its own name (CanvasPainter.Heading) — so the
            // panel and the block read the same. The rename box beneath this
            // still edits node.Name, not the bus suffix.
            Text = CanvasPainter.Heading(node, def),
            FontSize = Text.Title,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Right,
            Foreground = ink,
            Background = Brushes.Transparent,

            // Struck through while the module is switched off — see ADR-0117.
            TextDecorations = node.Off ? TextDecorations.Strikethrough : null,
        };

        // A name on a source-built module is the name the `let` gave it, so
        // renaming here would be renaming the wrong copy — the text would put
        // the old one back on the next apply.
        if (editor.History.Locked)
        {
            ToolTip.SetTip(title, "The text names this module. Rename it there.");
            return title;
        }

        ToolTip.SetTip(title, node.Name is null
            ? "Double-click to give this module a name of its own."
            : $"Double-click to rename. Empty the box to go back to '{def.Name}'.");

        // A name that can be changed says so under the pointer. Only here, because
        // a locked canvas's name is the text's and this one is not a button.
        title.Cursor = NameBox.Renaming;

        title.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            BeginRename(node, def, ink, title);
        };

        return title;
    }

    /// <summary>
    /// Swaps the name for a box to type one into. Enter keeps what was typed and so
    /// does clicking away; Escape abandons it; an empty box puts the module back to
    /// its definition's name.
    /// </summary>
    /// <remarks>
    /// The one place here that swaps a control in rather than rebuilding the panel:
    /// the box has to take the keyboard the moment it appears, which means being in
    /// the tree already, and it puts itself back from inside its own
    /// <c>LostFocus</c>.
    /// </remarks>
    private void BeginRename(NodeInstance node, NodeDef def, IBrush ink, Control title) =>
        NameBox.Open(
            title,
            ink,
            node.Name,
            def.Name,
            NodeInstance.NameLimit,
            typed => node.Rename(def, typed),
            () => node.Name,
            () => BuildTitle(node, def, ink),
            () => editor.History.Record());

    /// <summary>
    /// What is driving this module's unpatched sockets, and why nothing on the
    /// canvas shows it. Null where every socket is either patched or on a knob.
    /// </summary>
    /// <remarks>
    /// The absence is the part that needs explaining: everything else in this
    /// editor is visible in the patch, and a signal arriving from nowhere is not.
    /// Sockets that have since been patched drop out of the list, and
    /// <see cref="InspectorShape.Of"/> already counts a wire arriving as a reason to
    /// rebuild.
    /// </remarks>
    private Control? BuildNormalledNote(NodeInstance node, NodeDef def)
    {
        var reading = new List<string>();

        for (var i = 0; i < def.Inputs.Count; i++)
        {
            if (editor.History.Patch.IncomingTo(node.Id, i) is not null) continue;
            if (NodeCatalog.Normalled(def.Inputs[i]) is not { } source) continue;

            reading.Add($"'{def.Inputs[i].Name}' is reading {source}");
        }

        if (reading.Count == 0) return null;

        return new TextBlock
        {
            Text = Sentence(reading)
                 + ", with no wire to show for it: the module behind that is hidden, and one of "
                 + "it is shared by the whole patch. It is the unplugged jack of a rack, already "
                 + "carrying the signal you would have plugged in. Patch the socket to read "
                 + "something else instead — unplug it again and this comes back.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ModulePlate.BodyQuiet(def),
            FontSize = Text.Body,
            Margin = new Thickness(0, 0, 0, 6),
        };

        // "a", "a and b", "a, b and c" — an inspector is prose, and a list
        // joined with commas to the end reads as a table that lost its rules.
        static string Sentence(List<string> parts) => parts.Count switch
        {
            1 => parts[0],
            2 => $"{parts[0]} and {parts[1]}",
            _ => $"{string.Join(", ", parts[..^1])} and {parts[^1]}",
        };
    }

    /// <summary>
    /// Which control edits one of the things a module carries that is not a knob.
    /// </summary>
    /// <remarks>
    /// The one place the App knows the kinds apart, and a lookup here rather than a
    /// method on <see cref="NodeExtra"/> because the engine does not reference
    /// Avalonia. A kind with nothing here costs a row on the panel and not the
    /// panel.
    /// </remarks>
    private Control? EditorFor(NodeExtra extra, NodeInstance node, NodeDef def, bool reading) => extra switch
    {
        // A sequencer's tune is a list rather than a row of knobs (ADR-0038),
        // so it is edited as one — added to, taken from and reordered.
        StepsExtra steps => new StepList(
            node, steps.Spec, Colors.Palette(def).Accent, because => document.Edited(node, because)).View,

        // A quantiser's scale is a set rather than a sequence, so it is edited
        // as the octave it is a subset of rather than as a list of numbers.
        ScaleExtra => new ScaleKeys(node, def, because => document.Edited(node, because)).View,

        // The one a node carries that is not a number, so it is a name and a
        // button rather than a control with a range.
        SampleExtra => BuildSampleRow(node),
        PictureExtra => BuildPictureRow(node),

        // Anything else is a plugin's own kind, which ships no control and is
        // drawn from what it declares instead — see ADR-0055. A kind that
        // declares nothing simply gets no rows.
        _ => BuildDeclaredRows(node, extra, reading),
    };

    /// <summary>
    /// How the computer keyboard is laid out, on a MIDI In that listens to it.
    /// </summary>
    /// <remarks>
    /// The patch's setting rather than the module's (ADR-0099), shown here
    /// because this is where somebody playing the keys is looking. Every MIDI In
    /// on the keyboard shows the same one, and the heading says so, so that
    /// changing it on one and finding it changed on another is what was
    /// expected. Not shown on a module listening to a device, whose notes are
    /// its own.
    /// </remarks>
    /// <summary>
    /// The channels a MIDI In may listen to, as the tracks of the instrument it
    /// is listening to, or null where that instrument is not one Flyback knows.
    /// </summary>
    private IReadOnlyList<ChoiceOption>? TracksOf(NodeInstance node)
    {
        var device = new ExtraState(new MidiExtra().Fields, node.StateOf(MidiExtra.StateKey)).Chosen(MidiExtra.DeviceField);
        var source = midi.Sources.FirstOrDefault(s => s.Id == device);

        if (source.Id is null || instruments.For(source) is not { Tracks.Count: > 0 } profile) return null;

        return
        [
            new ChoiceOption("0", "Every channel"),
            .. profile.Tracks.Select(track => new ChoiceOption(
                track.Channel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"{track.Name} · channel {track.Channel}")),
        ];
    }

    private Control? BuildKeyboardSection(NodeInstance node, NodeDef def)
    {
        if (node.TypeId != NodeCatalog.MidiTypeId) return null;

        var device = new ExtraState(new MidiExtra().Fields, node.StateOf(MidiExtra.StateKey)).Chosen(MidiExtra.DeviceField);

        if (!string.IsNullOrWhiteSpace(device) && device != MidiSources.Keyboard) return null;

        var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };

        panel.Children.Add(new TextBlock
        {
            Text = "computer keyboard — the whole patch's, the same on every MIDI In",
            FontSize = Text.Micro,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var layout = new ExtraField.Choice(
            "layout",
            "layout",
            [new ChoiceOption(Piano, "Piano"), new ChoiceOption(ByScale, "Scale")],
            Piano);

        panel.Children.Add(Rows.ChoiceRow(
            layout,
            editor.History.Patch.KeyboardScale is null ? Piano : ByScale,
            picked =>
            {
                // A scale left behind is picked up again, so trying the piano
                // for a moment does not cost the notes that had been chosen.
                if (picked == ByScale) editor.History.Patch.KeyboardScale = [.. (IEnumerable<int>?)keptKeyboardScale ?? Major];
                else
                {
                    keptKeyboardScale = editor.History.Patch.KeyboardScale;
                    editor.History.Patch.KeyboardScale = null;
                }

                document.Relaid();

                // After the picker has finished with its own event, since what
                // is rebuilt includes the picker.
                Dispatcher.UIThread.Post(Build);
            }));

        if (editor.History.Patch.KeyboardScale is not null)
            panel.Children.Add(new ScaleKeys(
                Colors.Palette(def).Accent,
                () => [.. editor.History.Patch.KeyboardScale ?? []],
                scale =>
                {
                    editor.History.Patch.KeyboardScale = scale;
                    document.Relaid();
                    editor.History.Record();
                },
                played: true).View);

        return panel;
    }

    private const string Piano = "piano";
    private const string ByScale = "scale";

    /// <summary>What a fresh scale layout starts on: C major, what a fresh Quantiser starts on.</summary>
    internal static readonly int[] Major = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>The scale last switched away from, for switching back to.</summary>
    private List<int>? keptKeyboardScale;

    private InspectorRows? rows;

    /// <summary>The panel's editable rows, which report an edit to the canvas and the hand coming off to the text.</summary>
    private InspectorRows Rows => rows ??= new InspectorRows(because => editor.History.Record(because), document.HandCameOff);

    /// <summary>
    /// A plugin's extra, drawn from its <see cref="NodeExtra.Fields"/>.
    /// </summary>
    /// <remarks>
    /// Knowledge of the vocabulary rather than of any plugin: nothing here could
    /// tell you which one it is drawing. A field shape this build has never heard
    /// of is skipped rather than drawn wrongly.
    /// </remarks>
    private Control? BuildDeclaredRows(NodeInstance node, NodeExtra extra, bool reading)
    {
        if (extra.Fields.Count == 0) return null;

        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        panel.Children.Add(new TextBlock
        {
            Text = extra.Key,
            FontSize = Text.Micro,
            Foreground = Text.Muted,
            Margin = new Thickness(0, 0, 0, 4),
        });

        foreach (var field in extra.Fields)
            if (BuildFieldRow(node, extra, field, reading) is { } control)
                panel.Children.Add(Helped(control, field.Help));

        return panel;
    }

    private Control? BuildFieldRow(NodeInstance node, NodeExtra extra, ExtraField field, bool reading) => field switch
    {
        // A MIDI In's channel is a list of tracks where its instrument is known by name.
        ExtraField.Number number when extra is MidiExtra && field.Key == MidiExtra.ChannelField
            && TracksOf(node) is { } tracks => Rows.ChoiceRow(
                new ExtraField.Choice(field.Key, field.Label, tracks, "0"),
                ((int)number.Value(node.StateOf(extra.Key)?[field.Key])).ToString(System.Globalization.CultureInfo.InvariantCulture),
                next => Store(node, extra, field, JsonValue.Create(float.TryParse(next, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var channel) ? channel : 0f))),

        ExtraField.Number number => Rows.ValueRow(
            field.Label,
            number.Spec,
            number.Value(node.StateOf(extra.Key)?[field.Key]),
            $"{node.Id} {extra.Key} {field.Key}",
            next => Store(node, extra, field, JsonValue.Create(next)),
            reading),

        ExtraField.Toggle toggle => Rows.ToggleRow(
            field.Label,
            toggle.Value(node.StateOf(extra.Key)?[field.Key]),
            next => Store(node, extra, field, JsonValue.Create(next))),

        ExtraField.Choice choice => Rows.ChoiceRow(
            choice,
            choice.Value(node.StateOf(extra.Key)?[field.Key]),
            next =>
            {
                Store(node, extra, field, JsonValue.Create(next));

                // The keyboard's section belongs to a MIDI In on the keyboard,
                // and the channel row's shape to its instrument, so both come and
                // go with the device.
                if (extra is MidiExtra && field.Key == MidiExtra.DeviceField) Dispatcher.UIThread.Post(Build);
            },

            // What the same field would say if asked again. An extra is free to
            // compute its fields afresh — MidiExtra does, because what it lists
            // is what is plugged in — and this is how the list gets a second
            // chance to be right without the panel being rebuilt.
            () => extra.Fields
                .OfType<ExtraField.Choice>()
                .FirstOrDefault(again => again.Key == field.Key)?.Options ?? choice.Options),

        ExtraField.Text text => Rows.TextRow(
            text,
            text.Value(node.StateOf(extra.Key)?[field.Key]),
            next =>
            {
                if (node.TypeId != NodeCatalog.SendTypeId)
                {
                    Store(node, extra, field, JsonValue.Create(next));
                    return;
                }

                // A Send's only text is its bus, and its Receives go where it goes.
                foreach (var receive in BusEdits.Rename(editor.History.Patch, node, next)) document.Restated(receive.Id, field.Key);

                document.Restated(node.Id, field.Key);
            },

            // An Expression's formula is the one field whose text is a language,
            // so it is the one that can be marked as unread.
            node.TypeId == NodeCatalog.ExpressionTypeId ? NodeCatalog.FormulaProblem : null),

        _ => null,
    };

    /// <summary>
    /// Writes one field of a plugin's extra back, making the stored object first
    /// where the module arrived without one.
    /// </summary>
    /// <remarks>
    /// Through the field's own tidying, which is where an extra's range differs
    /// from a knob's: a socket's <see cref="PortSpec.Min"/> is the editor's
    /// suggestion and a saved value outside it widens the slider, where a field's
    /// range is what the value means.
    /// </remarks>
    private void Store(NodeInstance node, NodeExtra extra, ExtraField field, JsonNode value)
    {
        var held = extra.Stored(node.StateOf(extra.Key));
        held[field.Key] = field.Sane(value);

        node.SetState(extra.Key, held);

        // Noted rather than written, for the reason a knob is: a field on a
        // slider is dragged, and the text should be edited once at the end of it.
        document.Restated(node.Id, field.Key);
    }

    /// <summary>
    /// The sound file a player reads: what it is called, and a button to pick
    /// another.
    /// </summary>
    /// <remarks>
    /// The name alone rather than the whole path, with the full one on the tooltip
    /// — a file that has gone is found again by knowing where it was supposed to
    /// be. Nothing here says whether it could be read: that is the compiler's to
    /// say, in the status bar, naming the module.
    /// </remarks>
    private Control BuildSampleRow(NodeInstance node) => BuildFileRow(
        node,
        "file",
        SampleExtra.Of(node),
        "Choose a sound",
        SoundFileType,
        picked =>
        {
            SampleExtra.Set(node, picked);

            // Forgotten first, so a file that has been replaced since it was
            // last read is read again rather than answered from the cache.
            soundFolder.Forget(picked);
        });

    /// <summary>The same row for the other kind of file — see <see cref="PictureExtra"/>.</summary>
    private Control BuildPictureRow(NodeInstance node) => BuildFileRow(
        node,
        "picture",
        PictureExtra.Of(node),
        "Choose a picture",
        PictureFileType,
        picked =>
        {
            PictureExtra.Set(node, picked);
            pictureFolder.Forget(picked);
        });

    /// <summary>
    /// A file this instance carries: what it is called, what it currently is, and a
    /// button that goes and finds another. One row for both kinds, which differ in
    /// the picker's title, the label, the filter and what to do with what comes
    /// back.
    /// </summary>
    private Control BuildFileRow(
        NodeInstance node,
        string label,
        string? held,
        string title,
        FilePickerFileType kind,
        Action<string> store)
    {
        var chosen = held ?? string.Empty;

        var row = InspectorRows.Row("*,Auto");
        row.Margin = new Thickness(0, 8, 0, 0);

        var caption = InspectorRows.Caption(label);

        var name = new TextBlock
        {
            Text = chosen.Length == 0 ? "none chosen" : Path.GetFileName(chosen),
            FontSize = Text.Body,
            Opacity = chosen.Length == 0 ? 0.45 : 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0),
        };

        if (chosen.Length > 0) ToolTip.SetTip(name, chosen);

        var choose = new Button { Content = "Choose…", FontSize = Text.Small };

        choose.Click += async (_, _) =>
        {
            var files = await pickers.Open(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = [kind],
            });

            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } picked) return;

            store(picked);

            // The shape of the panel has not changed, so it is not rebuilt — the
            // one row that did is written here, the way a knob writes its own.
            name.Text = Path.GetFileName(picked);
            name.Opacity = 0.75;
            ToolTip.SetTip(name, picked);

            document.Edited(node);

            // Every other control in the panel is written into the text by the
            // hand coming off it, and the hand came off this button before the
            // dialog opened: the file arrives after that release, with nothing
            // left to flush it. Said here, because the gesture is over the
            // moment the picker answers.
            document.HandCameOff();
        };

        Grid.SetColumn(caption, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(choose, 2);

        row.Children.Add(caption);
        row.Children.Add(name);
        row.Children.Add(choose);

        return row;
    }

    /// <summary>What the sound picker offers, which is what the reader can read.</summary>
    private static FilePickerFileType SoundFileType => new("WAV audio")
    {
        Patterns = ["*.wav"],
        MimeTypes = ["audio/wav", "audio/x-wav"],
    };

    /// <summary>And what the picture picker offers, for the same reason.</summary>
    private static FilePickerFileType PictureFileType => new("PNG images")
    {
        Patterns = ["*.png"],
        MimeTypes = ["image/png"],
    };

    /// <param name="name">What the row is captioned, the socket's own name unless given.</param>
    private Control BuildInputRow(NodeDef def, NodeInstance node, PortSpec spec, int index, bool reading, string? name = null)
    {
        name ??= spec.Name;

        var patched = WireEnds.Into(editor.History.Patch, node.Id, index);

        var label = InspectorRows.Caption(name);

        var row = InspectorRows.KnobRow(reading);
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        if (patched is not null)
        {
            var wired = new TextBlock
            {
                Text = patched,
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(wired, 1);
            Grid.SetColumnSpan(wired, reading ? 3 : 2);
            row.Children.Add(wired);
            return row;
        }

        // A normalled socket has no knob to show. It is already carrying a
        // signal — one the patch does not draw, because there is no module on
        // the canvas for a wire to come from — so the row says which, in the
        // place a slider would have been. Why it is not a slider is the whole
        // point of it: there is nothing to set here until something is patched
        // in, and a control that did nothing would be worse than none.
        if (NodeCatalog.Normalled(spec) is { } normalled)
        {
            var implied = new TextBlock
            {
                Text = $"◀ {normalled}, without a wire",
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(implied, 1);
            Grid.SetColumnSpan(implied, reading ? 3 : 2);
            row.Children.Add(implied);
            return row;
        }

        // The other kind of normalled jack: an earlier socket on the same
        // module rather than a hidden one off it. Output's 'right' falls back
        // to 'left' this way, and the row names it exactly as it would a
        // module normalled off the canvas.
        if (spec.NormalledFrom is >= 0 and var from && from < def.Inputs.Count)
        {
            var implied = new TextBlock
            {
                Text = $"◀ {def.Inputs[from].Name}, without a wire",
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(implied, 1);
            Grid.SetColumnSpan(implied, reading ? 3 : 2);
            row.Children.Add(implied);
            return row;
        }

        // A socket with nothing worth a knob — see PortSpec.NeedsAWire. The
        // stored default still answers the compiler when nothing is patched,
        // it is just not a number anybody chose by dragging, so the row says
        // that plainly instead of offering a slider that would mislead.
        if (spec.NeedsAWire)
        {
            var unpatched = new TextBlock
            {
                Text = "◀ not patched",
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(unpatched, 1);
            Grid.SetColumnSpan(unpatched, reading ? 3 : 2);
            row.Children.Add(unpatched);
            return row;
        }

        if (ControlMap.Of(node, index) is { } link && editor.History.Patch.Control(link.Control) is { } knob)
            return LinkedRow(node, spec, name, index, link, knob);

        var value = index < node.InputValues.Length ? node.InputValues[index] : spec.Default;
        var (reads, flag) = RemapReading(def, node, index);

        // Named after the socket, so a slider dragged across its range is one
        // step to undo rather than one per frame of the drag.
        return Rows.ValueRow(name, spec, value, $"{node.Id} input {index}", next =>
        {
            if (index < node.InputValues.Length) node.InputValues[index] = next;

            // Noted rather than written. A drag is a knob turned a hundred times
            // and the text should be edited once, when the hand comes off it.
            document.Turned(node.Id, index);
        }, reading, reads, flag);
    }

    /// <summary>
    /// For one of an Auto remap's range knobs, what its fraction comes to at the
    /// far end of the wire, or why the pair is plain numbers; nothing for any other socket.
    /// </summary>
    private (Func<float, string>? Reads, string? Flag) RemapReading(NodeDef def, NodeInstance node, int index)
    {
        if (def.TypeId != NodeCatalog.AutoRemapTypeId || index == AutoRemap.In) return (null, null);

        var spans = AutoRemap.Of(editor.History.Patch, node);
        var input = index is AutoRemap.InLow or AutoRemap.InHigh;

        // Fractions of 0..1 are the numbers themselves, so an unwired side says nothing more.
        return (input ? spans.In : spans.Out) switch
        {
            { } span when span == RemapSpan.Unit => (null, null),
            { } span => (travel => span.Format(travel), null),
            null => (_ => "", $"Plain numbers: {(input ? spans.InWhy : spans.OutWhy)}."),
        };
    }

    /// <summary>
    /// A socket that follows a knob, in the inspector: which knob, the range it
    /// follows it over, and a button to let it go.
    /// </summary>
    private Control LinkedRow(NodeInstance node, PortSpec spec, string caption, int index, ControlLink link, PatchControl knob)
    {
        var row = InspectorRows.Row("*,58,14,58,26");

        var label = InspectorRows.Caption(caption);
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        var name = new TextBlock
        {
            Text = $"◉ {knob.Name}",
            FontSize = Text.Body,
            Foreground = new SolidColorBrush(Colors.Attention),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        ToolTip.SetTip(name, $"Follows the knob '{knob.Name}' on the knob panel, over this range.");

        var min = Bound(link.Min, next => link with { Min = next });
        var max = Bound(link.Max, next => link with { Max = next });

        var dash = new TextBlock
        {
            Text = "–",
            Foreground = Text.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var unlink = new Button
        {
            Name = "unlink",
            Content = "✕",
            Padding = new Avalonia.Thickness(0),
            Width = 22,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        ToolTip.SetTip(unlink, "Let this socket go, leaving it where the knob had put it.");

        unlink.Click += (_, _) =>
        {
            if (index < node.InputValues.Length) node.InputValues[index] = link.At(knob.Value);

            ControlMap.Unlink(node, index);
            editor.History.Record();
        };

        Grid.SetColumn(name, 1);
        Grid.SetColumn(min, 2);
        Grid.SetColumn(dash, 3);
        Grid.SetColumn(max, 4);
        Grid.SetColumn(unlink, 5);

        row.Children.Add(name);
        row.Children.Add(min);
        row.Children.Add(dash);
        row.Children.Add(max);
        row.Children.Add(unlink);

        return row;

        NumericUpDown Bound(float value, Func<float, ControlLink> with)
        {
            var box = new NumericUpDown
            {
                Value = Boxed.Of(value),
                Increment = spec.Stepped ? 1m : 0.05m,
                FormatString = spec.Stepped ? "0.##" : "0.###",
                FontSize = Text.Body,
                ShowButtonSpinner = false,
                VerticalAlignment = VerticalAlignment.Center,
            };

            box.ValueChanged += (_, e) =>
            {
                if (e.NewValue is not { } next) return;

                link = with((float)next);
                ControlMap.Link(node, index, link);

                // The range is part of what the panel takes its shape from, so
                // that one changed from elsewhere rebuilds this row. Changed from
                // here the row already says it, and rebuilding would take the box
                // out from under the number being typed into it — after its
                // first digit, a box taking its value a keystroke at a time.
                inspectorShape = InspectorShape.Of(editor);

                editor.History.Record($"{node.Id} range {index}");
            };

            return Boxed.NeverBlank(box);
        }
    }
}
