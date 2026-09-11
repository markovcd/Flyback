using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App;

/// <summary>
/// The panel on the right: the selected module's knobs, and — for the Output,
/// which every patch has — the settings of the instrument itself.
/// </summary>
/// <remarks>
/// Rebuilt from nothing every time the selection changes, because what it shows
/// is entirely the selected module's port list. The Output's own controls are the
/// exception and are wired once from the constructor: they are the state of the
/// instrument rather than of a selection, so they work before anything is
/// selected and keep their values across every rebuild.
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>
    /// What the panel says with nothing selected, which is where every gesture
    /// the canvas has is written down.
    /// </summary>
    /// <remarks>
    /// Adding a module is named first because the list opens where it is asked
    /// for, and the Output sits behind the preview so the controls stay
    /// discoverable.
    /// </remarks>
    private const string Help =
        "Right-click the canvas — or press Space — to add a module. "
        + "Type to narrow the list, arrows to move through it, Enter to add.\n\n"
        + "Select a module to edit its values, and double-click its "
        + "name here to call it something else.\n\n"
        + "Select the Output for the preview size, the renderer, "
        + "sound, and saving a frame or a clip.\n\n"
        + "Drag from a socket to patch it into another, or onto bare "
        + "canvas to add a module already plugged in.\n"
        + "Drag a connected input to unplug it and take the wire "
        + "somewhere else.\n"
        + "Ctrl+drag an output with one wire on it to feed that "
        + "wire from somewhere else instead.\n"
        + "Drag the background to select, middle-drag to pan, "
        + "wheel to zoom.\n"
        + "Ctrl+click adds to a selection, Ctrl+A takes everything.\n"
        + "Ctrl+C, Ctrl+X and Ctrl+V copy, cut and paste.\n"
        + "Ctrl+G draws a selection as one box and Ctrl+Shift+G "
        + "puts it back; double-click a box to open it.\n"
        + "Ctrl+E opens every box the selection touches at once, "
        + "Ctrl+Shift+E shuts them again.\n"
        + "Delete removes what is selected, Ctrl+F frames the patch.";

    /// <summary>
    /// The same, for a canvas that is a view of somebody's source rather than the
    /// patch itself — see ADR-0068. Everything that reads is here and everything
    /// that writes is gone; naming the gestures that are switched off would leave
    /// somebody concluding the program was broken.
    /// </summary>
    private const string LockedHelp =
        "The text is the document, and this is a view of what it builds. "
        + "Press F2 to go back to it — modules and wires are added and removed there, "
        + "and \"Edit on the canvas\" under the text hands the patch back so they can be "
        + "drawn here instead.\n\n"
        + "Select a module — on the canvas, or by putting the caret in the code where "
        + "it is written — to edit it here. Its knobs, its tune, its file: letting go "
        + "writes the new value into the code, where the code already says it.\n\n"
        + "Drag the background to select, middle-drag to pan, wheel to zoom.\n"
        + "Ctrl+click adds to a selection, Ctrl+A takes everything.\n"
        + "Ctrl+C copies what is selected, Ctrl+F frames the patch.";

    /// <summary>
    /// What the panel says for a caret standing on a module the patch has moved on
    /// from — see <see cref="Adrift"/>. Said rather than left blank: a panel that
    /// quietly stops cannot be told apart from a caret in the wrong place.
    /// </summary>
    private const string Adrifting =
        "The text has moved on from the patch that is playing, so this module is not "
        + "there to edit yet — the code names a module by where it stands, and something "
        + "typed in ahead of this one gives it a new name.\n\n"
        + "Apply the text to catch the patch up, or take the edit back. Modules the "
        + "edit did not move are still here to select.";

    private static readonly (string Label, PixelSize Size)[] Resolutions =
    [
        ("320 x 180", new PixelSize(320, 180)),
        ("480 x 270", new PixelSize(480, 270)),
        ("640 x 360", new PixelSize(640, 360)),
        ("960 x 540", new PixelSize(960, 540)),
        ("1280 x 720", new PixelSize(1280, 720)),
        ("1920 x 1080", new PixelSize(1920, 1080)),
    ];

    /// <summary>960 x 540: enough to judge a patch by, cheap enough to keep up.</summary>
    private const int DefaultResolution = 3;

    private const string GpuTip =
        "Render the picture with a shader instead of the processor. Turn it off to " +
        "compare the two, or if a long session starts to look stepped.";

    /// <summary>
    /// Sets up the controls that live on the Output's panel. Called once, from
    /// the constructor, rather than while building that panel: these are the
    /// state of the instrument and not of a selection, so they have to work
    /// before anything has been selected and keep their values after the panel
    /// showing them has been torn down and rebuilt.
    /// </summary>
    private void WireOutputControls()
    {
        resolution.SelectionChanged += (_, _) =>
        {
            if (resolution.SelectedIndex >= 0)
                preview.Resolution = Resolutions[resolution.SelectedIndex].Size;
        };
        preview.Resolution = Resolutions[DefaultResolution].Size;

        // On by default, because it is the one that keeps up with a large patch.
        // Turning it off is how two backends get compared, and the answer to a
        // long session drifting — see ADR-0035 on float32 and the phase
        // accumulator. It disables itself if the GPU turns out to be unusable.
        gpuButton.IsChecked = true;
        gpuButton.IsCheckedChanged += (_, _) =>
            preview.Use(gpuButton.IsChecked == true ? PreviewBackend.Gpu : PreviewBackend.Cpu);

        preview.BackendChanged += message =>
        {
            // The choice rather than what is running: a patch the shader cannot
            // draw puts the picture on the processor without anybody having
            // asked, and a button that unticked itself would then be read as the
            // setting having changed — and would change it, through this very
            // handler, the next time anything touched it.
            gpuButton.IsChecked = preview.Wanted == PreviewBackend.Gpu;
            gpuButton.IsEnabled = preview.GpuAvailable;
            ToolTip.SetTip(gpuButton, preview.GpuAvailable ? GpuTip : message);
            Report(message);
        };

        // The picture a take was reading has gone. Finishing the file is the only
        // useful thing left to do with it — what is already written is a
        // recording, and what would follow is the same frame for ever.
        preview.CaptureLost += Stop;

        // It cannot be switched on at all where no plugin offered a device. The
        // constructor turns it on once there is a patch to play — see there for
        // why it starts on rather than off.
        audioButton.IsEnabled = sound.Output is not null;
        ToolTip.SetTip(audioButton, sound.Output is { } output
            ? $"Play the patch through {output.Name}. Needs something wired into 'left'."
            : "No sound backend is installed. See About for where plugins are looked for.");
        audioButton.IsCheckedChanged += (_, _) => SetAudioEnabled(audioButton.IsChecked == true);

        ToolTip.SetTip(length, "How many seconds an export writes.");

        // Shown while it is greyed out too, because a disabled control that will
        // not say why is the most annoying thing a panel can contain.
        ToolTip.SetShowOnDisabled(exportButton, true);

        exportButton.Click += async (_, _) =>
        {
            // The same button stops it. An export is the one thing here that
            // runs long enough to be worth abandoning, and it is already the
            // control your eye is on.
            if (export is not null)
            {
                export.Cancel();
                return;
            }

            await ExportAsync();
        };

        ToolTip.SetShowOnDisabled(recordButton, true);

        recordButton.Click += async (_, _) =>
        {
            // The same button ends it. A take has no length, so stopping it is
            // the only way it ever finishes.
            if (recorder is not null)
            {
                Stop();
                return;
            }

            await RecordAsync();
        };

        BuildOutputSettings();
    }

    private Grid BuildRightPanel()
    {
        var grid = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(new GridLength(1, GridUnitType.Star)) { MinHeight = 140 },
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(1.1, GridUnitType.Star)) { MinHeight = 120 },
            ],
        };

        previewBox = new Border
        {
            Background = Brushes.Black,
            Child = preview,
        };

        // Double-click the picture and it takes the window; double-click it or
        // press Escape to put everything back. The gesture every video player
        // already has, on the one control here that is a video.
        previewBox.DoubleTapped += (_, e) =>
        {
            ToggleFullScreenPreview();
            e.Handled = true;
        };

        Grid.SetRow(previewBox, 0);

        previewRow = grid.RowDefinitions[0];

        var splitter = previewSplitter = new GridSplitter { Background = Brushes.Transparent, Height = 5 };
        Grid.SetRow(splitter, 1);

        // The mark sits behind the inspector rather than beside it, and never
        // takes a click — an empty panel is a better place for it than a corner
        // of the toolbar, and it is out of the way once there is something to read.
        var inspectorBorder = new Border
        {
            Background = new SolidColorBrush(Colors.Panel),
            Child = new Panel
            {
                Children =
                {
                    watermark,

                    // Explicitly transparent: a theme that gave the scroll
                    // viewer a background would paint straight over the mark.
                    new ScrollViewer { Content = inspector, Background = Brushes.Transparent },
                },
            },
        };
        Grid.SetRow(inspectorBorder, 2);

        grid.Children.Add(previewBox);
        grid.Children.Add(splitter);
        grid.Children.Add(inspectorBorder);

        return grid;
    }

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
    private void SyncInspector()
    {
        if (InspectorShape() != inspectorShape) BuildInspector();
    }

    /// <summary>
    /// The selected module and which of its inputs are patched, as one string to
    /// compare against the one the panel standing was built from.
    /// </summary>
    private string InspectorShape()
    {
        // A group's panel is its edge, and the edge moves whenever a wire is
        // drawn across it — which, like patching a module's input, is not a
        // selection change and would otherwise leave a stale list on screen.
        // Whether it is open is in here for the same reason: the button that
        // opens it says which way it goes.
        if (editor.SelectedGroup is { } group)
        {
            var sockets = editor.Patch.SocketsOf(group);

            // The name is in here as well, because the panel does not only show
            // it: the button that keeps a group in the module list is offered on
            // the strength of it, and is refused to a group with none. So a
            // rename has to rebuild this panel and not only the title in it.
            var shape = new StringBuilder($"g{group.Id:N}{(group.Collapsed ? 'c' : 'o')}{group.Name}");

            // Whether each is wired as well as which they are: a socket keeps its
            // row when the wire comes off, but it grows the button that takes it
            // off the edge — so unplugging changes the panel without changing
            // which sockets are on it.
            foreach (var socket in sockets.Inputs.Concat(sockets.Outputs))
                shape.Append(
                    $"{(socket.IsOutput ? 'o' : 'i')}{socket.Node:N}.{socket.Port}"
                    + $"{(editor.Patch.Wired(group, socket) ? '+' : '-')}");

            return shape.ToString();
        }

        if (editor.SelectedNode is not { } node || NodeCatalog.Get(node.TypeId) is not { } def)
            return string.Empty;

        var patched = new char[def.Inputs.Count];

        for (var i = 0; i < patched.Length; i++)
            patched[i] = editor.Patch.IncomingTo(node.Id, i) is null ? '.' : 'w';

        // Which groups the selection touches and which way round each is drawn,
        // because that is what the open and close buttons are offered on.
        var groups = new StringBuilder();

        foreach (var touched in editor.SelectedGroups)
            groups.Append($"{touched.Id:N}{(touched.Collapsed ? 'c' : 'o')}");

        return $"{node.Id:N}{new string(patched)}{groups}";
    }

    /// <summary>
    /// Rebuilt whenever the selection changes, and whenever a wire changes what
    /// the selected module's rows are. The canvas handles patching; exact
    /// numbers are easier to set with real controls than by dragging on a knob.
    /// </summary>
    private void BuildInspector()
    {
        inspectorShape = InspectorShape();
        inspector.Children.Clear();

        var selected = editor.SelectedNode is { } chosen && NodeCatalog.Get(chosen.TypeId) is not null;

        // What an empty panel says depends on which canvas is under it. Naming
        // gestures that are switched off would be worse than saying nothing: a
        // person following them would conclude the program was broken rather
        // than that the patch belongs to the text — see ADR-0068.

        // Louder with nothing in front of it, faint once there are values to
        // read. A watermark that competed with a column of sliders would be a
        // decoration in the way of the thing it decorates.
        watermark.Opacity = selected ? 0.06 : 0.14;

        if (editor.SelectedNode is not { } node || NodeCatalog.Get(node.TypeId) is not { } def)
        {
            inspector.Children.Add(new TextBlock
            {
                Text = adrift ? Adrifting : editor.Locked ? LockedHelp : Help,
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
        if (editor.SelectedGroup is { } group)
        {
            BuildGroupInspector(group);
            return;
        }

        inspector.Children.Add(BuildTitle(node, def));

        inspector.Children.Add(new TextBlock
        {
            Text = def.Category,
            FontSize = Text.Small,
            Foreground = new SolidColorBrush(Colors.Accent(def.Category)),
        });

        if (!string.IsNullOrEmpty(def.Description))
            inspector.Children.Add(new TextBlock
            {
                Text = def.Description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Text.Muted,
                FontSize = Text.Body,
                Margin = new Thickness(0, 4, 0, 6),
            });

        if (BuildNormalledNote(node, def) is { } normalled) inspector.Children.Add(normalled);

        // Checked once for the whole panel rather than row by row, so that a
        // bar is the same width down the entire module: a module where nothing
        // has a reading gives every slider the column back, and a module where
        // even one socket does reserves it for all of them, named or not.
        var reading = ShowsReading(def);

        for (var i = 0; i < def.Inputs.Count; i++)
            inspector.Children.Add(BuildInputRow(node, def.Inputs[i], i, reading));

        // Whatever the module carries that is not a knob, each kind edited by the
        // control that suits it. This mapping lives here rather than on the extra
        // because it is the one part of a kind that needs Avalonia, which the
        // engine does not reference.
        foreach (var extra in def.Extras)
            if (EditorFor(extra, node, def, reading) is { } control)
                inspector.Children.Add(control);

        if (def.Inputs.Count == 0 && def.Extras.Count == 0)
            inspector.Children.Add(new TextBlock
            {
                Text = "This module has nothing to set — it only produces.",
                Foreground = Text.Muted,
                FontSize = Text.Body,
            });

        // Everything about seeing and hearing the patch hangs off the one module
        // that does both, instead of being spread along a toolbar. Nothing here
        // is saved with the patch — a preview size is a property of the machine
        // you are working on, not of the instrument — but this is where you go
        // looking for it, because this is the block it acts on.
        if (NodeCatalog.IsSink(node.TypeId))
        {
            inspector.Children.Add(outputSettings);
            return;
        }

        // What the graph is made of belongs to whoever owns it. A knob turned on
        // a locked canvas is written back into the text (ADR-0068); a module
        // deleted from one could not be, so the button is not offered rather
        // than offered and undone by the next apply.
        if (editor.Locked) return;

        // Grouping sits above deleting rather than beside it, so the destructive
        // button keeps the place a hand already knows.
        //
        // Its label counts the way delete's does — see NodeEditor.Groupable — and
        // it is offered on the same terms Ctrl+G is: a button reading "Group 1
        // module" would offer something the graph refuses. Ungrouping is not here,
        // because a selection that is exactly a group gets a panel of its own.
        if (editor.Groupable >= NodeGroup.Fewest)
            Act($"Group {editor.Groupable} modules", editor.GroupSelected, 14);

        // For a selection that reaches into groups without being one: the group
        // panel above answers only a selection that is exactly one, and a
        // double-click only the box it lands on.
        var shut = editor.SelectedGroups.Count(g => g.Collapsed);
        var open = editor.SelectedGroups.Count(g => !g.Collapsed);

        if (shut > 0)
            Act(shut > 1 ? $"Open {shut} groups" : "Open group", editor.OpenSelectedGroups, 8);

        if (open > 0)
            Act(open > 1 ? $"Close {open} groups" : "Close group", editor.CloseSelectedGroups, 8);

        // Delete takes the whole selection, the same as the key does, so the
        // label counts it. Sinks are left out of the count because the graph
        // refuses them: a button offering to delete three when it can only
        // manage two would be lying about what pressing it does.
        var going = editor.SelectedNodes.Count(n => !NodeCatalog.IsSink(n.TypeId));

        Act(going > 1 ? $"Delete {going} modules" : "Delete module", editor.DeleteSelected, 14);

        void Act(string caption, Action gesture, double above)
        {
            var button = new Button
            {
                Content = caption,
                Margin = new Thickness(0, above, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            button.Click += (_, _) => gesture();
            inspector.Children.Add(button);
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
        inspector.Children.Add(BuildGroupTitle(group));

        inspector.Children.Add(new TextBlock
        {
            Text = group.Name is null ? "Group" : $"Group · {group.Counted}",
            FontSize = Text.Small,
            Foreground = Text.Muted,
        });

        inspector.Children.Add(new TextBlock
        {
            Text = "Several modules drawn as one. Nothing about the patch changes — the modules "
                 + "are where they were and so are the wires between them.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Text.Muted,
            FontSize = Text.Body,
            Margin = new Thickness(0, 4, 0, 6),
        });

        var sockets = editor.Patch.SocketsOf(group);

        Edge("In", sockets.Inputs);
        Edge("Out", sockets.Outputs);

        if (sockets.Rows == 0)
            inspector.Children.Add(new TextBlock
            {
                Text = "Nothing has been wired across its edge, so the box has no sockets yet.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Text.Muted,
                FontSize = Text.Body,
            });

        // A box on a locked canvas is drawn from the text's group statements, so
        // everything that would change one is left off for the same reason
        // deleting a module is — see BuildInspector.
        if (editor.Locked) return;

        Act(group.Collapsed ? "Open group" : "Close group", editor.ToggleSelectedGroup, 14);

        // Keeping one is not an edit to the patch, so it sits with the two that
        // are not either and above the two that are. What the module list will
        // call it is its name and nothing else — so a group with none is offered
        // the button greyed rather than a button that saves "3 modules" under a
        // heading full of other things called "3 modules". The way out is one
        // gesture up: the title at the top of this panel renames on a
        // double-click.

        // The button hands itself to what it does, because the answer to it may
        // have to be asked in the place the button is standing — see KeepGroup.
        Button keep = null!;

        keep = Act("Save to palette", () => KeepGroup(group, keep), 8);
        keep.IsEnabled = !string.IsNullOrWhiteSpace(group.Name);

        // Which of the two things pressing it does, said before it is pressed.
        // Replacing is the one worth knowing about in advance — it is somebody
        // else's group going, and the name is the only warning there is.
        ToolTip.SetTip(keep, !keep.IsEnabled
            ? "The module list calls a kept group by its name — double-click the title above to give it one."
            : groups?.Named(group.Name) is not null
                ? $"Replaces the “{group.Name}” already in the module list. It will ask first."
                : $"Keeps “{group.Name}” under Groups in the module list, ready to add again.");

        // The greyed one is precisely the one with something to explain, and a
        // tip that will not show on a disabled control explains it to nobody.
        // The two buttons in the toolbar above that grey themselves out do the
        // same for the same reason.
        ToolTip.SetShowOnDisabled(keep, true);

        Act("Ungroup", editor.UngroupSelected, 8);
        Act($"Delete {group.Members.Count} modules", editor.DeleteSelected, 8);

        // One heading and a row per socket, each named for the module and port
        // inside that it stands for — which is exactly what the box draws, so
        // the panel and the canvas read the same.
        void Edge(string heading, IReadOnlyList<GroupSocket> sockets)
        {
            if (sockets.Count == 0) return;

            inspector.Children.Add(new TextBlock
            {
                Text = heading,
                FontSize = Text.Small,
                FontWeight = FontWeight.SemiBold,
                Opacity = 0.7,
                Margin = new Thickness(0, 10, 0, 2),
            });

            foreach (var socket in sockets)
                if (editor.Named(socket) is var (label, spec))
                    inspector.Children.Add(Socket(socket, label, spec));
        }

        // A row, and — on one with nothing plugged into it — the way to take it
        // off the edge again. Only there while it is unwired: a socket a wire is
        // on comes back the moment anything asks, so a button offering to remove
        // one would appear to do nothing.
        Control Socket(GroupSocket socket, string label, PortSpec spec)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 1) };

            var text = new TextBlock
            {
                Text = label,
                FontSize = Text.Body,
                Opacity = 0.85,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.PortColor(spec.Kind)),
            };

            if (!editor.Patch.Wired(group, socket) && !editor.Locked)
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
                remove.Click += (_, _) => editor.HideSocket(group, socket);

                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
            }

            row.Children.Add(text);
            return row;
        }

        Button Act(string caption, Action gesture, double above)
        {
            var button = new Button
            {
                Content = caption,
                Margin = new Thickness(0, above, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            button.Click += (_, _) => gesture();
            inspector.Children.Add(button);

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
    /// to lose one. Asked in the place the button was standing, the way the module
    /// list asks about a row that is going: a dialog would be right if this could
    /// lose work, and what it can lose is one entry in a list.
    /// </remarks>
    private void KeepGroup(NodeGroup group, Button keep)
    {
        if (groups is null || string.IsNullOrWhiteSpace(group.Name)) return;

        var at = inspector.Children.IndexOf(keep);

        // Nothing kept under that name, or no button left to ask in — either way
        // there is nothing to ask about.
        if (groups.Named(group.Name) is null || at < 0)
        {
            SaveGroup(group);
            return;
        }

        inspector.Children[at] = Question.Row(
            $"Replace “{group.Name}”?",
            keep.Margin,
            $"Replace the kept “{group.Name}” with this group.",
            "Leave the kept one alone.",
            replace =>
            {
                if (replace) SaveGroup(group);

                // Put back exactly what a fresh panel would have, which is the
                // button reading whatever it should read now — a replaced group
                // is one this list already knows, so its tip changes.
                BuildInspector();
            });
    }

    /// <summary>
    /// A question and its two answers on one row: the tick acts, the cross backs
    /// out. The same shape and glyphs the module list uses to ask about a row it is
    /// told to forget — small, immediate, and about the thing under it.
    /// </summary>

    private Control BuildGroupTitle(NodeGroup group)
    {
        var title = new TextBlock
        {
            Text = group.Title(),
            FontSize = Text.Title,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Background = Brushes.Transparent,
        };

        ToolTip.SetTip(title, group.Name is null
            ? "Double-click to give this group a name of its own."
            : $"Double-click to rename. Empty the box to go back to '{group.Counted}'.");

        title.DoubleTapped += (_, e) =>
        {
            e.Handled = true;

            BeginRename(
                title,
                group.Name,
                group.Counted,
                NodeGroup.NameLimit,
                typed => group.Rename(typed),
                () => group.Name,
                () => BuildGroupTitle(group));
        };

        return title;
    }

    private Control BuildTitle(NodeInstance node, NodeDef def)
    {
        var title = new TextBlock
        {
            Text = node.Title(def),
            FontSize = Text.Title,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Background = Brushes.Transparent,
        };

        // A name on a source-built module is the name the `let` gave it, so
        // renaming here would be renaming the wrong copy — the text would put
        // the old one back on the next apply.
        if (editor.Locked)
        {
            ToolTip.SetTip(title, "The text names this module. Rename it there.");
            return title;
        }

        ToolTip.SetTip(title, node.Name is null
            ? "Double-click to give this module a name of its own."
            : $"Double-click to rename. Empty the box to go back to '{def.Name}'.");

        title.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            BeginRename(node, def, title);
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
    private void BeginRename(NodeInstance node, NodeDef def, Control title) =>
        BeginRename(
            title,
            node.Name,
            def.Name,
            NodeInstance.NameLimit,
            typed => node.Rename(def, typed),
            () => node.Name,
            () => BuildTitle(node, def));

    /// <summary>
    /// Turns a title into a box to type another name into, and puts the title back
    /// when the box closes.
    /// </summary>
    /// <remarks>
    /// Written against what a name is rather than against what carries one, because
    /// two things carry one: a module and a group. Enter keeping, Escape
    /// discarding, losing the focus keeping, and only real edits reaching the
    /// history are the same for both.
    /// </remarks>
    /// <param name="title"></param>
    /// <param name="held">The name it has, which is null on one nobody has named.</param>
    /// <param name="fallback">What it is called when it has no name of its own.</param>
    /// <param name="limit"></param>
    /// <param name="rename">Takes what was typed, with whatever tidying the thing does to one.</param>
    /// <param name="current">The name as it stands, read again afterwards to see whether it moved.</param>
    /// <param name="rebuild">The title to put back.</param>
    private void BeginRename(
        Control title,
        string? held,
        string fallback,
        int limit,
        Action<string?> rename,
        Func<string?> current,
        Func<Control> rebuild)
    {
        var at = inspector.Children.IndexOf(title);
        if (at < 0) return;

        var box = new TextBox
        {
            // The name it has, not the one it shows. Opening this on something
            // nobody has renamed leaves an empty box, because empty is what it
            // means — and what it would go back to is in the watermark, where it
            // reads as the thing you would get rather than as text to delete
            // before typing.
            Text = held ?? string.Empty,
            PlaceholderText = fallback,
            MaxLength = limit,
            FontSize = Text.Title,
            FontWeight = FontWeight.SemiBold,
        };

        // Enter takes the focus off the box as it closes it, which would bring
        // the focus handler round a second time. Every way out goes through the
        // one flag instead.
        var closed = false;

        box.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter: Close(keep: true); break;
                case Key.Escape: Close(keep: false); break;
                default: return;
            }

            e.Handled = true;
        };

        box.LostFocus += (_, _) => Close(keep: true);

        inspector.Children[at] = box;

        box.Focus();
        box.SelectAll();

        void Close(bool keep)
        {
            if (closed) return;
            closed = true;

            var before = current();
            if (keep) rename(box.Text);

            var where = inspector.Children.IndexOf(box);
            if (where >= 0) inspector.Children[where] = rebuild();

            // Only where it is actually a rename: the canvas draws its headers
            // from the same name and this is what redraws them, and a step in
            // the history for opening a box and closing it again would be one
            // press of undo that puts nothing back.
            if (current() != before) editor.NotifyPatchChanged();
        }
    }

    /// <summary>
    /// The screen-and-speakers half of the Output's panel: what the picture is
    /// rendered at and by, and the four ways of getting either of them out.
    /// </summary>
    /// <remarks>
    /// Built once and kept, not rebuilt per selection: these controls hold live
    /// state, and a control may have one parent at a time.
    /// </remarks>
    private void BuildOutputSettings()
    {
        outputSettings.Children.Add(Heading("Picture"));
        outputSettings.Children.Add(Field("Size", resolution));
        outputSettings.Children.Add(Field("Render", gpuButton));

        outputSettings.Children.Add(Heading("Sound"));
        outputSettings.Children.Add(audioButton);

        // Under Sound rather than a heading of its own, though it moves both
        // halves: a rewind that took the picture back and left the sound where it
        // was would pull apart one instrument on one timeline.
        //
        // The width is the audio button's, because everything standalone here sits
        // at its left edge and is only as wide as it needs to be — the two being
        // the same width says they are a pair.
        var rewind = new Button { Content = "Rewind", Width = 92 };

        rewind.Click += (_, _) =>
        {
            audio.Rewind();
            preview.Rewind();
        };

        ToolTip.SetTip(rewind, "Take the patch back to zero seconds, in the picture and in the sound.");

        outputSettings.Children.Add(rewind);

        outputSettings.Children.Add(Heading("Export"));

        // The seconds box with its unit beside it, so the number is not a bare
        // one on a panel where every other number is in patch units.
        outputSettings.Children.Add(Field("Length", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { length, Label("seconds") },
        }));

        outputSettings.Children.Add(exportButton);
        outputSettings.Children.Add(recordButton);
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = Text.Caption,
        FontWeight = FontWeight.SemiBold,
        Foreground = Text.Muted,
        Margin = new Thickness(0, 10, 0, 2),
    };

    /// <summary>A labelled row on the same 78-pixel gutter the knob rows use.</summary>
    /// <summary>
    /// How wide the column every row puts its name in is. One number rather than
    /// seven, because it is stated twice per row — as the grid column and as the
    /// caption's own width, so a name too long to fit is trimmed at the gutter
    /// rather than pushing the control along.
    /// </summary>
    private const double Gutter = 78;

    /// <summary>A row's name, in the gutter every row shares.</summary>
    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        Width = Gutter,
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>The gutter, and whatever columns the caller needs beside it.</summary>
    private static Grid Row(string beside) =>
        new() { ColumnDefinitions = new ColumnDefinitions($"{Gutter},{beside}") };

    /// <summary>
    /// A knob whose number is not what it means, and so wants a column for what it
    /// does mean: "57" is not what anyone means by the note they are picking. A
    /// count needs no such column, but lands on whole numbers for the same reason a
    /// note does.
    /// </summary>
    private static bool Named(PortSpec spec) => spec.Display != PortDisplay.Number;

    /// <summary>Whether any socket or field on this module has a reading to show.</summary>
    /// <remarks>
    /// Checked once for a module rather than once per row, because the answer
    /// governs a column every row on the panel shares — see <see cref="KnobRow"/>.
    /// </remarks>
    private static bool ShowsReading(NodeDef def) =>
        def.Inputs.Any(Named) ||
        def.Extras.Any(extra => extra.Fields
            .Any(field => field is ExtraField.Number number && Named(number.Spec)));

    /// <summary>A knob's row: the slider, the reading if it has one, and its number.</summary>
    /// <remarks>
    /// <paramref name="reading"/> reserves the column for the whole panel rather
    /// than this row, so every slider on a module is the same width and no row
    /// reserves it when nothing on the module has a reading. 60 fits the widest
    /// there is: a duration just under a second, as "999.9 ms".
    /// </remarks>
    private static Grid KnobRow(bool reading) => Row(reading ? "*,60,84" : "*,84");

    private static Control Field(string name, Control control)
    {
        var row = Row("*");

        var label = Caption(name);

        Grid.SetColumn(label, 0);
        Grid.SetColumn(control, 1);

        row.Children.Add(label);
        row.Children.Add(control);

        return row;
    }

    /// <summary>
    /// What is driving this module's unpatched sockets, and why nothing on the
    /// canvas shows it. Null where every socket is either patched or on a knob.
    /// </summary>
    /// <remarks>
    /// The absence is the part that needs explaining: everything else in this
    /// editor is visible in the patch, and a signal arriving from nowhere is not.
    /// Sockets that have since been patched drop out of the list, and
    /// <see cref="InspectorShape"/> already counts a wire arriving as a reason to
    /// rebuild.
    /// </remarks>
    private Control? BuildNormalledNote(NodeInstance node, NodeDef def)
    {
        var reading = new List<string>();

        for (var i = 0; i < def.Inputs.Count; i++)
        {
            if (editor.Patch.IncomingTo(node.Id, i) is not null) continue;
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
            Foreground = Text.Muted,
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
        StepsExtra steps => new StepList(node, steps.Spec, because => Edited(node, because)).View,

        // A quantiser's scale is a set rather than a sequence, so it is edited
        // as the octave it is a subset of rather than as a list of numbers.
        ScaleExtra => new ScaleKeys(node, def, because => Edited(node, because)).View,

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
                panel.Children.Add(control);

        return panel;
    }

    private Control? BuildFieldRow(NodeInstance node, NodeExtra extra, ExtraField field, bool reading) => field switch
    {
        ExtraField.Number number => ValueRow(
            field.Label,
            number.Spec,
            number.Value(node.StateOf(extra.Key)?[field.Key]),
            $"{node.Id} {extra.Key} {field.Key}",
            next => Store(node, extra, field, JsonValue.Create(next)),
            reading),

        ExtraField.Toggle toggle => ToggleRow(
            field.Label,
            toggle.Value(node.StateOf(extra.Key)?[field.Key]),
            next => Store(node, extra, field, JsonValue.Create(next))),

        ExtraField.Choice choice => ChoiceRow(
            choice,
            choice.Value(node.StateOf(extra.Key)?[field.Key]),
            next => Store(node, extra, field, JsonValue.Create(next)),

            // What the same field would say if asked again. An extra is free to
            // compute its fields afresh — MidiExtra does, because what it lists
            // is what is plugged in — and this is how the list gets a second
            // chance to be right without the panel being rebuilt.
            () => extra.Fields
                .OfType<ExtraField.Choice>()
                .FirstOrDefault(again => again.Key == field.Key)?.Options ?? choice.Options),

        _ => null,
    };

    /// <summary>
    /// A label and a list to pick from, on the same grid a knob's row uses.
    /// </summary>
    /// <remarks>
    /// What is stored may not be in the list — a device switched off, a patch
    /// written on another machine — and that is shown rather than corrected: an
    /// entry for it is added at the end, so the picker shows what the patch means.
    /// <paramref name="fresh"/> is asked again as the list opens, which is the one
    /// moment it matters; a list that changed under a pointer already inside it
    /// would move the row somebody was reaching for.
    /// </remarks>
    private Control ChoiceRow(
        ExtraField.Choice choice,
        string value,
        Action<string> store,
        Func<IReadOnlyList<ChoiceOption>>? fresh = null)
    {
        var row = Row("*");

        var caption = Caption(choice.Label);

        // What is stored is always in the list, whether or not it is here.
        List<ChoiceOption> Offer(IReadOnlyList<ChoiceOption> from)
        {
            var offered = from.ToList();

            if (offered.All(option => option.Id != value))
                offered.Add(new ChoiceOption(value, choice.Name(value)));

            return offered;
        }

        var options = Offer(choice.Options);

        var list = new Picker
        {
            ItemsSource = options,
            DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ChoiceOption.Name)),
            SelectedIndex = options.FindIndex(option => option.Id == value),
            FontSize = Text.Body,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not ChoiceOption picked || picked.Id == value) return;

            // Discrete, so it names no gesture: picking again is picking again
            // rather than one pick going on. Named, two of them would fold into
            // one step — and a pick that came back to where it started would
            // leave a step that puts nothing back.
            store(picked.Id);
            editor.NotifyPatchChanged();
        };

        if (fresh is not null)
            list.DropDownOpened += (_, _) =>
            {
                var offered = Offer(fresh());

                // Left alone when nothing has changed, which is nearly always.
                // Replacing the items clears the selection on the way past, and
                // doing that for no reason is how a picker loses its place.
                if (offered.Select(option => option.Id).SequenceEqual(options.Select(option => option.Id))) return;

                options = offered;

                list.ItemsSource = options;
                list.SelectedIndex = options.FindIndex(option => option.Id == value);
            };

        Grid.SetColumn(caption, 0);
        Grid.SetColumn(list, 1);
        row.Children.Add(caption);
        row.Children.Add(list);

        return row;
    }

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
        Restated(node.Id, field.Key);
    }

    /// <summary>A label and a switch, laid out on the same grid a knob's row uses.</summary>
    private Control ToggleRow(string label, bool value, Action<bool> store)
    {
        var row = Row("*");

        var caption = Caption(label);

        var box = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };

        box.IsCheckedChanged += (_, _) =>
        {
            // Discrete, for the reason a picker is: on and off again are two
            // things done rather than one held down, and folding them would
            // leave a step whose patch is the one already showing.
            store(box.IsChecked == true);
            editor.NotifyPatchChanged();
        };

        Grid.SetColumn(caption, 0);
        Grid.SetColumn(box, 1);
        row.Children.Add(caption);
        row.Children.Add(box);

        return row;
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

        var row = Row("*,Auto");
        row.Margin = new Thickness(0, 8, 0, 0);

        var caption = Caption(label);

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
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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

            Edited(node);

            // Every other control in the panel is written into the text by the
            // hand coming off it, and the hand came off this button before the
            // dialog opened: the file arrives after that release, with nothing
            // left to flush it. Said here, because the gesture is over the
            // moment the picker answers.
            HandCameOff();
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

    private Control BuildInputRow(NodeInstance node, PortSpec spec, int index, bool reading)
    {
        var connected = editor.Patch.IncomingTo(node.Id, index) is not null;

        var label = Caption(spec.Name);

        var row = KnobRow(reading);
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        if (connected)
        {
            var wired = new TextBlock
            {
                Text = "◀ patched",
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
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

        var value = index < node.InputValues.Length ? node.InputValues[index] : spec.Default;

        // Named after the socket, so a slider dragged across its range is one
        // step to undo rather than one per frame of the drag.
        return ValueRow(spec.Name, spec, value, $"{node.Id} input {index}", next =>
        {
            if (index < node.InputValues.Length) node.InputValues[index] = next;

            // Noted rather than written. A drag is a knob turned a hundred times
            // and the text should be edited once, when the hand comes off it.
            Turned(node.Id, index);
        }, reading);
    }

    /// <summary>
    /// A label, a slider, a number box, and — where the number is not what it means
    /// — what it does mean written beside them.
    /// </summary>
    /// <remarks>
    /// Shared by a socket's knob and by a plugin's declared number field, so a
    /// plugin gets snapping, formatting and the widened range for nothing. The
    /// caller says where the value lives; nothing here knows whether that is an
    /// input array or a stored object.
    /// </remarks>
    /// <param name="label">What to write in the left column.</param>
    /// <param name="spec">The range, the display and whether it snaps.</param>
    /// <param name="value">What it starts at.</param>
    /// <param name="because">
    /// What to file the edit under, so dragging is one undo step rather than one
    /// per frame.
    /// </param>
    /// <param name="store">Where the new value goes.</param>
    /// <param name="reading">
    /// Whether the panel reserves a column for a reading at all — see
    /// <see cref="ShowsReading"/>. A row whose socket has nothing to say there
    /// still gets the column when a neighbor needs it.
    /// </param>
    private Control ValueRow(
        string label,
        PortSpec spec,
        float value,
        string because,
        Action<float> store,
        bool reading)
    {
        var named = Named(spec);
        var whole = spec.Stepped;

        var row = KnobRow(reading);

        var caption = Caption(label);

        Grid.SetColumn(caption, 0);
        row.Children.Add(caption);

        var slider = new Slider
        {
            // Widen the range if a saved value sits outside the module's usual span.
            Minimum = Math.Min(spec.Min, value),
            Maximum = Math.Max(spec.Max, value),
            Value = value,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0),

            // Dragging a note or a count lands on whole numbers. The module
            // quantises whatever it is given anyway, so a slider that stopped
            // between two would only be showing a distinction the patch does
            // not have.
            IsSnapToTickEnabled = whole,
            TickFrequency = 1,
        };

        var numeric = new NumericUpDown
        {
            Value = (decimal)value,
            Increment = whole ? 1m : 0.05m,
            FormatString = whole ? "0.##" : "0.###",
            FontSize = Text.Body,
            VerticalAlignment = VerticalAlignment.Center,
            ShowButtonSpinner = false,
        };

        var name = new TextBlock
        {
            Text = spec.Format(value),
            FontSize = Text.Body,
            Opacity = 0.75,
            Margin = new Thickness(6, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var updating = false;

        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && e.NewValue is double d)
                Apply((float)d);
        };

        numeric.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } d) Apply((float)d);
        };

        Grid.SetColumn(slider, 1);
        Grid.SetColumn(numeric, reading ? 3 : 2);
        row.Children.Add(slider);
        row.Children.Add(numeric);

        if (named)
        {
            Grid.SetColumn(name, 2);
            row.Children.Add(name);
        }

        return row;

        void Apply(float next)
        {
            if (updating) return;

            updating = true;
            store(next);
            slider.Value = next;
            numeric.Value = (decimal)next;
            name.Text = spec.Format(next);
            updating = false;

            editor.NotifyPatchChanged(because);
        }
    }
}
