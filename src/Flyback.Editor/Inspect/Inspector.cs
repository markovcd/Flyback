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

    /// <summary>The panel a group gets instead of a module's.</summary>
    private readonly GroupInspector groups;

    /// <summary>The rows for the files a module carries.</summary>
    private readonly FileRows files;

    /// <summary>The rows for the fields a plugin's module declares.</summary>
    private readonly FieldRows fieldRows;

    /// <summary>The rows for the computer keyboard, on a MIDI In listening to it.</summary>
    private readonly KeyboardSection keyboard;

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
        groups = new GroupInspector(editor, panel, wash, plateHost, palette, socketRows, measuredRows);
        keyboard = new KeyboardSection(editor, document, rows);

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
            groups.Build(group);
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

        var actions = ActionRows.Module(editor);

        plate.Under.Children.Insert(above, actions);
        if (ActionRows.Selection(editor) is { } taking) plate.Under.Children.Insert(above + 1, taking);

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
