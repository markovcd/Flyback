using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Flyback.Editor.Assist;
using Flyback.Editor.Canvas;
using Flyback.Ui.Controls;
using Flyback.Editor.Notices;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
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
        IReactTo<Touched>,
        IReactTo<OwnershipChanged>
{
    private readonly NodeEditor editor;
    private readonly TextWriteBack writeBack;
    private readonly CaretFollow caret;
    private readonly PatchHeader header;
    private readonly MeasuredRows measuredRows;
    private readonly SocketRows socketRows;
    private readonly FileRows files;
    private readonly FieldRows fieldRows;
    private readonly KeyboardSection keyboard;

    /// <summary>The panel a group gets instead of a module's.</summary>
    private readonly GroupInspector groups;

    private readonly Renamer renamer;

    /// <summary>The shortcut groups left unfolded, kept across rebuilds so a selection does not shut them.</summary>
    private readonly HashSet<string> openShortcuts = ["Getting started"];

    /// <summary>Whether the editor is in a page, which has no files, settings or recording for the help to name.</summary>
    private readonly bool inPage;

    /// <summary>Whether a finger has touched the canvas, so the help names what a finger does.</summary>
    private bool fingers;

    public Inspector(
        NodeEditor editor,
        TextWriteBack writeBack,
        CaretFollow caret,
        EditorHost host,
        InspectorSurface surface,
        Renamer renamer,
        PatchHeader header,
        MeasuredRows measuredRows,
        SocketRows socketRows,
        FileRows files,
        FieldRows fieldRows,
        KeyboardSection keyboard,
        GroupInspector groups)
    {
        this.editor = editor;
        this.writeBack = writeBack;
        this.caret = caret;
        this.header = header;
        this.measuredRows = measuredRows;
        this.socketRows = socketRows;
        this.files = files;
        this.fieldRows = fieldRows;
        this.keyboard = keyboard;
        this.groups = groups;
        inPage = host.InPage;
        Panel = surface.Panel;
        Wash = surface.Wash;
        PlateHost = surface.PlateHost;
        Header = surface.Header;
        this.renamer = renamer;

        socketRows.Settled += (_, _) => inspectorShape = InspectorShape.Of(editor);
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

    /// <summary>A knob turned here on a patch the text owns is written back into the text, which the panel says.</summary>
    public Task On(OwnershipChanged notice)
    {
        Build();

        ToolTip.SetTip(
            Panel,
            notice.Owned
                ? "The text is the document. A knob turned here is written back into it "
                  + "where it already says it."
                : null);

        return Task.CompletedTask;
    }

    /// <summary>The rows, which scroll.</summary>
    public StackPanel Panel { get; }

    /// <summary>Shows the other of each measured color's two pictures, for a test that cannot wait for the timer.</summary>
    internal void TurnPictures() => measuredRows.Turn();

    /// <summary>The block's face, behind the whole column.</summary>
    public ModuleWash Wash { get; }

    /// <summary>The plate, heading the rows.</summary>
    public ContentControl PlateHost { get; }

    /// <summary>The plate folded to one pinned line.</summary>
    public InspectorHeader Header { get; }

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
        Panel.Children.Clear();
        measuredRows.Clear();
        PlateHost.Content = null;

        // What an empty panel says depends on which canvas is under it. Naming
        // gestures that are switched off would be worse than saying nothing: a
        // person following them would conclude the program was broken rather
        // than that the patch belongs to the text — see ADR-0068.
        if (editor.Selection.Focused is not { } node || NodeCatalog.Get(node.TypeId) is not { } def)
        {
            Wash.Clear();
            PlateHost.Content = null;

            foreach (var part in header.Build()) Panel.Children.Add(part);

            if (caret.IsAdrift || editor.History.Locked)
            {
                Panel.Children.Add(new TextBlock
                {
                    Text = caret.IsAdrift
                        ? caret.IsAdriftBox ? InspectorHelp.AdriftingGroup : InspectorHelp.Adrifting
                        : InspectorHelp.Locked(fingers, editor.Gestures.DragToPan),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Text.Muted,
                    FontSize = Text.Body,
                });
            }
            else
            {
                Panel.Children.Add(ShortcutList.Of(InspectorHelp.Shortcuts(inPage, fingers, editor.Gestures.DragToPan), openShortcuts));
            }

            return;
        }

        // A selection that is exactly a group is about the group, not about
        // whichever of its modules the pointer last came down on. Ahead of
        // everything below, because none of it applies: a box has no knobs, no
        // description and no category — what it has is an edge.
        if (editor.Selection.Group is { } group)
        {
            groups.Build(group);
            return;
        }

        // The block's own face, so the panel and the canvas are plainly the same
        // module. Its name and what can be done to it stand on the plate; the rows
        // below stay on the panel, which is what a column of numbers is read on.
        var plate = ModulePlate.Of(def);

        Wash.Show(def);
        Wash.Off = node.Off;

        plate.Named.Children.Add(BuildTitle(node, def, plate.Ink));

        plate.Named.Children.Add(new TextBlock
        {
            Text = def.Category,
            FontSize = Text.Small,
            Foreground = plate.Quiet,
            TextAlignment = TextAlignment.Left,
        });

        PlateHost.Content = plate;

        // What can be done to the module goes under its name, above the
        // description — see ActionRow. Where it goes is settled here and what is
        // in it at the end, because a knob does not decide whether a module can
        // be grouped.
        var above = plate.Under.Children.Count;

        plate.Face = new PlateFace(
            CanvasPainter.Heading(node, def),
            def.Category,
            ModuleGlyphs.For(def),
            new SolidColorBrush(Colors.Palette(def).Accent),
            def.Description,
            editor.History.Locked
                ? null
                : new PlateName(() => node.Name, def.Name, NodeInstance.NameLimit, typed => node.Rename(def, typed), () => editor.History.Record()));

        if (!editor.History.Locked)
            plate.BeginRename = () =>
            {
                if (plate.Named.Children.OfType<TextBlock>().FirstOrDefault(t => t.Name == "moduleName") is { } title)
                    BeginRename(node, def, plate.Ink, title);
            };

        if (!string.IsNullOrEmpty(def.Description))
            Panel.Children.Add(new TextBlock
            {
                Name = InspectorFold.DescriptionName,
                Text = def.Description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ModulePlate.BodyQuiet(def),
                FontSize = Text.Body,
                Margin = new Thickness(0, 4, 0, 6),
            });

        if (BuildNormalledNote(node, def) is { } normalled) Panel.Children.Add(normalled);

        // Checked once for the whole panel rather than row by row, so that a
        // bar is the same width down the entire module: a module where nothing
        // has a reading gives every slider the column back, and a module where
        // even one socket does reserves it for all of them, named or not.
        var reading = InspectorRows.ShowsReading(def) || def.TypeId == NodeCatalog.AutoRemapTypeId;

        for (var i = 0; i < def.Inputs.Count; i++)
            Panel.Children.Add(socketRows.Edged(InspectorRows.Helped(socketRows.Input(def, node, def.Inputs[i], i, reading), def.Inputs[i].Help), node, i, output: false));

        // Whatever the module carries that is not a knob, each kind edited by the
        // control that suits it. This mapping lives here rather than on the extra
        // because it is the one part of a kind that needs Avalonia, which the
        // engine does not reference.
        foreach (var extra in def.Extras)
            if (EditorFor(extra, node, def, reading) is { } control)
                Panel.Children.Add(extra.Fields.Count == 0 ? InspectorRows.Helped(control, extra.Help) : control);

        if (keyboard.Build(node) is { } laid) Panel.Children.Add(laid);

        if (def.Inputs.Count == 0 && def.Extras.Count == 0)
            Panel.Children.Add(new TextBlock
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

        var actions = ActionRows.Module(editor);

        plate.Under.Children.Insert(above, actions);
        if (ActionRows.Selection(editor) is { } taking) plate.Under.Children.Insert(above + 1, taking);

        Undescribed();

        // Last, under everything the module has: it is a note about the
        // assistant, and the one thing on the panel not about the patch.
        void Undescribed()
        {
            if (!editor.Tags.Types.Contains(def.TypeId)) return;

            Panel.Children.Add(new TextBlock
            {
                Name = "undescribedNote",
                Text = AssistantPanel.UndescribedNote,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Text.Muted,
                FontSize = Text.Small,
                Margin = new Thickness(0, 18, 0, 0),
            });
        }
    }

    /// <summary>
    /// What the module puts out, under everything that can be set: a heading and a
    /// row per output, each with its help as the tip and what it feeds beside it.
    /// </summary>
    private void BuildOutputs(NodeDef def, NodeInstance node)
    {
        if (def.Outputs.Count == 0) return;

        Panel.Children.Add(new TextBlock
        {
            Text = "Outputs",
            FontSize = Text.Small,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.7,
            Margin = new Thickness(0, 10, 0, 2),
        });

        for (var i = 0; i < def.Outputs.Count; i++)
        {
            Panel.Children.Add(socketRows.Edged(InspectorRows.Helped(socketRows.Output(node, def.Outputs[i].Name, i), def.Outputs[i].Help), node, i, output: true));

            foreach (var shown in measuredRows.Under(node, i)) Panel.Children.Add(shown);
        }
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
            TextAlignment = TextAlignment.Left,
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
        renamer.Open(
            title,
            ink,
            $"{CanvasPainter.Heading(node, def)} · {def.Category}",
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
            node, steps.Spec, Colors.Palette(def).Accent, because => writeBack.Edited(node, because)).View,

        // A quantiser's scale is a set rather than a sequence, so it is edited
        // as the octave it is a subset of rather than as a list of numbers.
        ScaleExtra => new ScaleKeys(node, def, because => writeBack.Edited(node, because)).View,

        // An Arrangement's parts are a grid, read as a shape and written a row at a time.
        ArrangementExtra => new PartGrid(node, def, because => writeBack.Edited(node, because)).View,

        // The one a node carries that is not a number, so it is a name and a
        // button rather than a control with a range.
        FileExtra file => files.Row(node, file),

        // Anything else is a plugin's own kind, which ships no control and is
        // drawn from what it declares instead — see ADR-0055. A kind that
        // declares nothing simply gets no rows.
        _ => fieldRows.Declared(node, extra, reading),
    };
}
