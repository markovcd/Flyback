using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flyback.Editor.Assist;
using Flyback.Editor.Bars;
using Flyback.Editor.Canvas;
using Flyback.Editor.Controls;
using Flyback.Engine.Graph;
using Flyback.Engine.Measure;
using Flyback.Ui.Controls;
using Flyback.Editor.Knobs;
using Flyback.Ui.Midi;
using Flyback.Editor.Notices;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Render;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Inspect;

/// <summary>
/// The panel on the right: the selected module's knobs, or the group's, or what
/// the patch says about itself when nothing is selected (ADR-0148).
/// </summary>
/// <remarks>
/// Rebuilt from nothing every time the selection changes, because what it shows
/// is entirely the selected module's port list.
/// </remarks>
internal sealed class Inspector
    : IReactTo<PatchChanged>,
        IReactTo<SelectionChanged>,
        IReactTo<InputLetGo>,
        IReactTo<PanelStale>,
        IReactTo<UndescribedChanged>,
        IReactTo<DocumentArrived>,
        IReactTo<DocumentSaved>,
        IReactTo<Touched>
{
    /// <summary>
    /// How far the panel's rows keep off its edges. Named because the plate at the
    /// head of it takes the inset back off again to reach them.
    /// </summary>
    internal const double PanelInset = 12;

    private readonly NodeEditor editor;
    private readonly MeasuredRows measuredRows;

    private readonly Document document;
    private readonly PatchHeader header;

    /// <summary>The panel's editable rows, which report an edit to the canvas and the hand coming off to the text.</summary>
    private readonly InspectorRows rows;

    /// <summary>The rows for a module's sockets.</summary>
    private readonly SocketRows socketRows;

    /// <summary>The rows for the files a module carries.</summary>
    private readonly FileRows files;

    /// <summary>The rows for the fields a plugin's module declares.</summary>
    private readonly FieldRows fieldRows;

    /// <summary>The rows for the computer keyboard, on a MIDI In listening to it.</summary>
    private readonly KeyboardSection keyboard;
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

    /// <summary>The shortcut groups left unfolded, kept across rebuilds so a selection does not shut them.</summary>
    private readonly HashSet<string> openShortcuts = ["Getting started"];

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
    /// <summary>Whether the editor is in a page, which has no files, settings or recording for the help to name.</summary>
    private readonly bool inPage;

    /// <summary>Whether a finger has touched the canvas, so the help names what a finger does.</summary>
    private bool fingers;

    public Inspector(NodeEditor editor, Document document, MidiHub midi, PanelKnobs knobs, PatchFiles files, Palette palette, IFilePickers pickers, EditorHost host, MeasureLabels measured)
    {
        measuredRows = new MeasuredRows(measured, panel);
        inPage = host.InPage;
        this.editor = editor;
        this.document = document;
        header = new PatchHeader(editor, document, files);
        this.files = new FileRows(pickers, document, files.SoundFolder, files.PictureFolder);
        rows = new InspectorRows(because => editor.History.Record(because), document.HandCameOff);
        socketRows = new SocketRows(editor, document, rows);
        socketRows.Settled += (_, _) => inspectorShape = InspectorShape.Of(editor);
        fieldRows = new FieldRows(editor, document, midi, knobs.Instruments, rows);
        keyboard = new KeyboardSection(editor, document, rows);
        groups = () => palette.Groups;
        saveGroup = palette.SaveGroup;

        // A drag on a slider is one edit, written into the text once the hand is off it.
        panel.AddHandler(InputElement.PointerReleasedEvent, (_, _) => document.HandCameOff(), RoutingStrategies.Bubble, handledEventsToo: true);
        panel.AddHandler(InputElement.LostFocusEvent, (_, _) => document.HandCameOff(), RoutingStrategies.Bubble);
        panel.AddHandler(InputElement.KeyUpEvent, (_, _) => document.HandCameOff(), RoutingStrategies.Bubble, handledEventsToo: true);
        panel.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => document.HandCameOff(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public Task On(PatchChanged notice)
    {
        measuredRows.Dim();

        Sync();
        return Task.CompletedTask;
    }

    public Task On(SelectionChanged notice)
    {
        Build();
        return Task.CompletedTask;
    }

    public Task On(PanelStale notice)
    {
        Build();
        return Task.CompletedTask;
    }

    public Task On(DocumentArrived notice)
    {
        header.Rename();
        return Task.CompletedTask;
    }

    public Task On(DocumentSaved notice)
    {
        header.Rename();
        return Task.CompletedTask;
    }

    public Task On(UndescribedChanged notice)
    {
        Build();
        return Task.CompletedTask;
    }

    public Task On(Touched notice)
    {
        fingers = true;
        if (editor.Selection.Focused is null) Build();
        return Task.CompletedTask;
    }

    /// <summary>A socket turned on the canvas shows its new value, where the panel is about its module.</summary>
    public Task On(InputLetGo notice)
    {
        var (node, _) = notice.Pick;

        if (editor.Selection.Focused?.Id == node || editor.Selection.Group?.Members.Contains(node) == true) Build();

        return Task.CompletedTask;
    }

    /// <summary>The rows, which scroll.</summary>
    public StackPanel Panel => panel;

    /// <summary>Shows the other of each measured color's two pictures, for a test that cannot wait for the timer.</summary>
    internal void TurnPictures() => measuredRows.Turn();

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
        measuredRows.Clear();
        plateHost.Content = null;

        // What an empty panel says depends on which canvas is under it. Naming
        // gestures that are switched off would be worse than saying nothing: a
        // person following them would conclude the program was broken rather
        // than that the patch belongs to the text — see ADR-0068.
        if (editor.Selection.Focused is not { } node || NodeCatalog.Get(node.TypeId) is not { } def)
        {
            wash.Clear();
            plateHost.Content = null;

            foreach (var part in header.Build()) panel.Children.Add(part);

            if (document.IsAdrift || editor.History.Locked)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = document.IsAdrift
                        ? document.IsAdriftBox ? InspectorHelp.AdriftingGroup : InspectorHelp.Adrifting
                        : InspectorHelp.Locked(fingers, editor.Gestures.DragToPan),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Text.Muted,
                    FontSize = Text.Body,
                });
            }
            else
            {
                panel.Children.Add(ShortcutList.Of(InspectorHelp.Shortcuts(inPage, fingers, editor.Gestures.DragToPan), openShortcuts));
            }

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
            panel.Children.Add(socketRows.Edged(InspectorRows.Helped(socketRows.Input(def, node, def.Inputs[i], i, reading), def.Inputs[i].Help), node, i, output: false));

        // Whatever the module carries that is not a knob, each kind edited by the
        // control that suits it. This mapping lives here rather than on the extra
        // because it is the one part of a kind that needs Avalonia, which the
        // engine does not reference.
        foreach (var extra in def.Extras)
            if (EditorFor(extra, node, def, reading) is { } control)
                panel.Children.Add(extra.Fields.Count == 0 ? InspectorRows.Helped(control, extra.Help) : control);

        if (keyboard.Build(node) is { } laid) panel.Children.Add(laid);

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

        plate.Under.Children.Insert(above, actions);
        if (SelectionRow() is { } taking) plate.Under.Children.Insert(above + 1, taking);

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
        {
            panel.Children.Add(socketRows.Edged(InspectorRows.Helped(socketRows.Output(node, def.Outputs[i].Name, i), def.Outputs[i].Help), node, i, output: true));

            foreach (var shown in measuredRows.Under(node, i)) panel.Children.Add(shown);
        }
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
    /// What the selection can be taken somewhere with: the clipboard's copy and cut, and
    /// laying out the selection alone. Each is a key or a held Ctrl for a hand that has one.
    /// </summary>
    private StackPanel? SelectionRow()
    {
        var row = ActionRow();
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
        const double socketGutter = 140;

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
        if (SelectionRow() is { } taking) plate.Under.Children.Insert(above + 1, taking);

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

            if (!socket.IsOutput || measuredRows.Under(node, socket.Port).ToList() is not { Count: > 0 } under) return helped;

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

        // An Arrangement's parts are a grid, read as a shape and written a row at a time.
        ArrangementExtra => new PartGrid(node, def, because => document.Edited(node, because)).View,

        // The one a node carries that is not a number, so it is a name and a
        // button rather than a control with a range.
        SampleExtra => files.Sample(node),
        PictureExtra => files.Picture(node),
        MidiFileExtra => files.MidiFile(node),

        // Anything else is a plugin's own kind, which ships no control and is
        // drawn from what it declares instead — see ADR-0055. A kind that
        // declares nothing simply gets no rows.
        _ => fieldRows.Declared(node, extra, reading),
    };
}
