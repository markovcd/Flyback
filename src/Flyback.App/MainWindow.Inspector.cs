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
/// which every patch has and which nothing else stands in for — the settings of
/// the instrument itself.
/// </summary>
/// <remarks>
/// Rebuilt from nothing every time the selection changes, because what it shows
/// is entirely the selected module's port list. That is the region ADR-0016
/// leans on hardest: under XAML it would be an ItemsControl, a template per port
/// kind, a selector and a view model per row, where here it is a method that
/// returns controls.
/// <para>
/// The Output's own controls are the exception and are wired once from the
/// constructor. They are the state of the instrument rather than of a selection,
/// so they have to work before anything is selected and keep their values across
/// every rebuild of the panel showing them.
/// </para>
/// </remarks>
public sealed partial class MainWindow
{
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
            : "No sound backend is installed. See the status bar for where plugins are looked for.");
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

        var splitter = new GridSplitter { Background = Brushes.Transparent, Height = 5 };
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
    /// What the panel's rows <em>are</em>, as against what is in them: which
    /// module is being shown, and which of its inputs have a wire on them.
    /// </summary>
    /// <remarks>
    /// A row is a knob or the word "patched" and never both, so a wire landing
    /// on the module already selected changes the panel as much as selecting a
    /// different one does. Nothing else the canvas reports does — a knob turned
    /// is a value and not a row — which is what keeps a slider mid-drag from
    /// being torn down under the hand holding it.
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

        return $"{node.Id:N}{new string(patched)}";
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

        // Louder with nothing in front of it, faint once there are values to
        // read. A watermark that competed with a column of sliders would be a
        // decoration in the way of the thing it decorates.
        watermark.Opacity = selected ? 0.06 : 0.14;

        if (editor.SelectedNode is not { } node || NodeCatalog.Get(node.TypeId) is not { } def)
        {
            inspector.Children.Add(new TextBlock
            {
                // Adding a module is named first, and no longer because it is
                // important: the list now opens where it is asked for, and the
                // Output sits behind the preview so the controls stay discoverable.
                Text = "Right-click the canvas — or press Space — to add a module. "
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
                     + "Delete removes what is selected, Ctrl+F frames the patch.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.5,
                FontSize = 12,
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
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.Accent(def.Category)),
        });

        if (!string.IsNullOrEmpty(def.Description))
            inspector.Children.Add(new TextBlock
            {
                Text = def.Description,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.6,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 6),
            });

        if (BuildNormalledNote(node, def) is { } normalled) inspector.Children.Add(normalled);

        for (var i = 0; i < def.Inputs.Count; i++)
            inspector.Children.Add(BuildInputRow(node, def.Inputs[i], i));

        // Whatever the module carries that is not a knob, each kind edited by the
        // control that suits it. This mapping lives here rather than on the extra
        // because it is the one part of a kind that needs Avalonia, which the
        // engine does not reference.
        foreach (var extra in def.Extras)
            if (EditorFor(extra, node, def) is { } control)
                inspector.Children.Add(control);

        if (def.Inputs.Count == 0 && def.Extras.Count == 0)
            inspector.Children.Add(new TextBlock
            {
                Text = "This module has nothing to set — it only produces.",
                Opacity = 0.5,
                FontSize = 12,
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

        // Grouping sits above deleting rather than beside it, so the destructive
        // button keeps the place it has always had: a panel that grows a control
        // should not move the one a hand already knows where to find.
        //
        // Its label counts the same way delete's does, off the same rule — see
        // NodeEditor.Groupable — and it is offered on the same terms Ctrl+G is,
        // which is NodeGroup.Fewest and up. A button reading "Group 1 module"
        // would be offering something the graph refuses.
        //
        // Ungrouping is not here, because a selection that is exactly a group
        // never reaches this far — it gets a panel of its own above.
        if (editor.Groupable >= NodeGroup.Fewest)
            Act($"Group {editor.Groupable} modules", editor.GroupSelected, 14);

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
    /// The name at the top of the panel, which a double-click turns into a box
    /// to type another one into.
    /// </summary>
    /// <remarks>
    /// A module is a thing on a canvas before it is a type, and a patch with
    /// four Mixers in it is one you have to follow a wire to read. The name is
    /// only ever a label — nothing is found by it, and two modules called the
    /// same thing is no more a problem than two called nothing.
    /// <para>
    /// Transparent rather than unpainted, because a <see cref="TextBlock"/> with
    /// no background of any kind is not there as far as the pointer is
    /// concerned, and the double-click would land on the panel behind it.
    /// </para>
    /// </remarks>
    /// <summary>
    /// What a group shows: its name, its edge, and what can be done to it.
    /// </summary>
    /// <remarks>
    /// The edge rather than the contents, and that is the whole point of the
    /// panel. A box is a promise that several modules can be thought about as one
    /// thing, and the only way to keep it is to say what that one thing takes and
    /// gives — a list of the knobs inside would be the box admitting it was never
    /// really one module at all. Open it to reach those.
    /// </remarks>
    private void BuildGroupInspector(NodeGroup group)
    {
        inspector.Children.Add(BuildGroupTitle(group));

        inspector.Children.Add(new TextBlock
        {
            Text = group.Name is null ? "Group" : $"Group · {group.Counted}",
            FontSize = 11,
            Opacity = 0.6,
        });

        inspector.Children.Add(new TextBlock
        {
            Text = "Several modules drawn as one. Nothing about the patch changes — the modules "
                 + "are where they were and so are the wires between them.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.6,
            FontSize = 12,
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
                Opacity = 0.5,
                FontSize = 12,
            });

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
                FontSize = 11,
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
                FontSize = 12,
                Opacity = 0.85,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.PortColor(spec.Kind)),
            };

            if (!editor.Patch.Wired(group, socket))
            {
                var remove = new Button
                {
                    Content = "✕",
                    FontSize = 10,
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
    /// Keeps a group in the module list, asking first where doing so would
    /// replace one already kept under that name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replacing is what saving under a name already taken means, and it is also
    /// somebody's group going for good — a file deleted, with nothing on this
    /// side of it to undo. The two are not in tension: it still replaces, it
    /// just does not do it because a hand was in the neighbourhood of a button.
    /// A name typed a second time by accident is the ordinary way to lose one,
    /// and the panel offers no clue that this is what the press would do.
    /// </para>
    /// <para>
    /// Asked in the place the button was standing, the way the module list asks
    /// about the row that is going: no sheet over the window, nothing to move
    /// the panel under the hand, and the question at the height the answer will
    /// be given. A dialog would be right if this could lose work — it cannot,
    /// and what it can lose is one entry in a list of them.
    /// </para>
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

        inspector.Children[at] = Ask(
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
    /// out.
    /// </summary>
    /// <remarks>
    /// The same shape and the same two glyphs the module list uses to ask about
    /// a row it is being told to forget, because it is the same kind of
    /// question — small, immediate, and about the thing directly under it. Two
    /// words on two buttons would be a dialog with the frame left off.
    /// </remarks>
    private static Control Ask(
        string question, Thickness margin, string yesTip, string noTip, Action<bool> answered)
    {
        var row = new DockPanel { Margin = margin };

        var answers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };

        answers.Children.Add(Answer("✔", yesTip, 1, () => answered(true)));
        answers.Children.Add(Answer("✕", noTip, 0.55, () => answered(false)));

        DockPanel.SetDock(answers, Dock.Right);
        row.Children.Add(answers);

        row.Children.Add(new TextBlock
        {
            Text = question,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });

        return row;

        static Button Answer(string glyph, string tip, double strength, Action taken)
        {
            var button = new Button
            {
                Content = glyph,
                FontSize = 11,
                Padding = new Thickness(6, 2),
                Background = Brushes.Transparent,
                Opacity = strength,
                VerticalAlignment = VerticalAlignment.Center,
            };

            ToolTip.SetTip(button, tip);
            button.Click += (_, _) => taken();

            return button;
        }
    }

    private Control BuildGroupTitle(NodeGroup group)
    {
        var title = new TextBlock
        {
            Text = group.Title(),
            FontSize = 17,
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
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Background = Brushes.Transparent,
        };

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
    /// Swaps the name for a box to type one into. Enter keeps what was typed and
    /// so does clicking away; Escape abandons it; and an empty box is how the
    /// module goes back to being called whatever its definition calls it.
    /// </summary>
    /// <remarks>
    /// The one place here that swaps a control in rather than rebuilding the
    /// panel around a flag. The box has to take the keyboard the moment it
    /// appears, which means being in the tree already, and it puts itself back
    /// from inside its own <c>LostFocus</c> — where tearing down the panel that
    /// is raising the event is more than this row needs to do.
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
    /// Turns a title into a box to type another name into, and puts the title
    /// back when the box closes.
    /// </summary>
    /// <remarks>
    /// Written against what a name <em>is</em> rather than against what carries
    /// one, because two things carry one now: a module and a group. Everything
    /// that made this worth getting right — Enter keeping, Escape discarding,
    /// losing the focus keeping, and only the edits that changed something
    /// reaching the history — is the same for both, and a second copy of it would
    /// be a second place for one of those to stop being true.
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
            FontSize = 17,
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
    /// Built once and kept, not rebuilt per selection like the knob rows above
    /// it. These controls hold live state, and a control may have one parent at
    /// a time — putting <see cref="resolution"/> into a freshly made row on
    /// every selection would leave it owned by the row before.
    /// </remarks>
    private void BuildOutputSettings()
    {
        outputSettings.Children.Add(Heading("Picture"));
        outputSettings.Children.Add(Field("Size", resolution));
        outputSettings.Children.Add(Field("Render", gpuButton));

        outputSettings.Children.Add(Heading("Sound"));
        outputSettings.Children.Add(audioButton);

        // Under Sound rather than under a heading of its own, though it moves
        // both halves: a rewind that took the picture back and left the sound
        // where it was would pull the two apart, and they are one instrument on
        // one timeline.
        //
        // The width is the audio button's. Everything standalone on this panel
        // sits at its left edge and is only as wide as it needs to be, so one
        // control stretched to the far side reads as a misalignment rather than
        // as emphasis — and these two being the same width says they are a pair.
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
        FontSize = 10.5,
        FontWeight = FontWeight.SemiBold,
        Opacity = 0.6,
        Margin = new Thickness(0, 10, 0, 2),
    };

    /// <summary>A labelled row on the same 78-pixel gutter the knob rows use.</summary>
    private static Control Field(string name, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("78,*") };

        var label = new TextBlock
        {
            Text = name,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(control, 1);

        row.Children.Add(label);
        row.Children.Add(control);

        return row;
    }

    /// <summary>
    /// What is driving this module's unpatched sockets, and why nothing on the
    /// canvas shows it. Null where every socket is either patched or on a knob,
    /// which is most of the catalogue.
    /// </summary>
    /// <remarks>
    /// Said here rather than only in the row, because the row can say which
    /// module and not why there is no wire from it. The absence is the part that
    /// needs explaining: everything else in this editor is visible in the patch,
    /// and a signal arriving from nowhere is the one thing that is not.
    /// <para>
    /// Sockets that have since been patched drop out of the list, which is what
    /// makes this a description of the module as it stands rather than of the
    /// module as catalogued. <see cref="InspectorShape"/> already counts a wire
    /// arriving as a reason to rebuild, so it keeps up on its own.
    /// </para>
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
            Opacity = 0.6,
            FontSize = 12,
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
    /// The one place the App knows the kinds apart, and the reason it is a lookup
    /// here rather than a method on <see cref="NodeExtra"/>: the rest of what a
    /// kind does lives in the engine, and the engine does not reference Avalonia.
    /// <para>
    /// A kind with nothing here shows nothing rather than throwing, so a plugin
    /// that carries state this build has never heard of costs it a row on the
    /// panel and not the panel.
    /// </para>
    /// </remarks>
    private Control? EditorFor(NodeExtra extra, NodeInstance node, NodeDef def) => extra switch
    {
        // A sequencer's tune is a list rather than a row of knobs (ADR-0038),
        // so it is edited as one — added to, taken from and reordered.
        StepsExtra steps =>
            new StepList(node, steps.Spec, because => editor.NotifyPatchChanged(because)).View,

        // A quantiser's scale is a set rather than a sequence, so it is edited
        // as the octave it is a subset of rather than as a list of numbers.
        ScaleExtra =>
            new ScaleKeys(node, def, because => editor.NotifyPatchChanged(because)).View,

        // The one a node carries that is not a number, so it is a name and a
        // button rather than a control with a range.
        SampleExtra => BuildSampleRow(node),
        PictureExtra => BuildPictureRow(node),

        // Anything else is a plugin's own kind, which ships no control and is
        // drawn from what it declares instead — see ADR-0055. A kind that
        // declares nothing gets no rows, which is the same nothing an
        // unrecognised one used to get.
        _ => BuildDeclaredRows(node, extra),
    };

    /// <summary>
    /// A plugin's extra, drawn from its <see cref="NodeExtra.Fields"/>.
    /// </summary>
    /// <remarks>
    /// The whole of the App's knowledge of a plugin's state is here, and it is
    /// knowledge of the vocabulary rather than of any plugin: nothing in this
    /// method could tell you which plugin it is drawing. A field shape this build
    /// has never heard of is skipped rather than drawn wrongly, so a patch made
    /// by a newer build stays editable in the parts this one understands.
    /// </remarks>
    private Control? BuildDeclaredRows(NodeInstance node, NodeExtra extra)
    {
        if (extra.Fields.Count == 0) return null;

        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        panel.Children.Add(new TextBlock
        {
            Text = extra.Key,
            FontSize = 9.5,
            Opacity = 0.4,
            Margin = new Thickness(0, 0, 0, 4),
        });

        foreach (var field in extra.Fields)
            if (BuildFieldRow(node, extra, field) is { } control)
                panel.Children.Add(control);

        return panel;
    }

    private Control? BuildFieldRow(NodeInstance node, NodeExtra extra, ExtraField field) => field switch
    {
        ExtraField.Number number => ValueRow(
            field.Label,
            number.Spec,
            number.Value(node.StateOf(extra.Key)?[field.Key]),
            $"{node.Id} {extra.Key} {field.Key}",
            next => Store(node, extra, field, JsonValue.Create(next))),

        ExtraField.Toggle toggle => ToggleRow(
            field.Label,
            toggle.Value(node.StateOf(extra.Key)?[field.Key]),
            $"{node.Id} {extra.Key} {field.Key}",
            next => Store(node, extra, field, JsonValue.Create(next))),

        ExtraField.Choice choice => ChoiceRow(
            choice,
            choice.Value(node.StateOf(extra.Key)?[field.Key]),
            $"{node.Id} {extra.Key} {field.Key}",
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
    /// What is stored may not be in the list — a device that is switched off, a
    /// patch written on another machine — and that is shown rather than
    /// corrected: an entry for it is added at the end, named the way
    /// <see cref="ExtraField.Choice.Name"/> writes one that is not here, so the
    /// picker shows what the patch actually means. Picking anything else drops
    /// it, which is the only way it goes.
    /// <para>
    /// <paramref name="fresh"/> is asked again as the list is opened, which is
    /// the one moment it matters: a MIDI keyboard plugged in while this panel was
    /// already on screen would otherwise not be there to pick, and clicking on
    /// another module and back is not an obvious thing to be asked to do. Only
    /// the opening — a list that changed under a pointer already inside it would
    /// move the row somebody was reaching for.
    /// </para>
    /// </remarks>
    private Control ChoiceRow(
        ExtraField.Choice choice,
        string value,
        string because,
        Action<string> store,
        Func<IReadOnlyList<ChoiceOption>>? fresh = null)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("78,*") };

        var caption = new TextBlock
        {
            Text = choice.Label,
            Width = 78,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

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
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not ChoiceOption picked || picked.Id == value) return;

            store(picked.Id);
            editor.NotifyPatchChanged(because);
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
    /// where the module arrived without one — a patch written before the plugin
    /// declared this field, or edited by hand into a shape that has no room for
    /// it.
    /// </summary>
    /// <remarks>
    /// Through the field's own tidying, so a declared range is what is stored and
    /// not merely what the slider offered. This is where an extra's range differs
    /// from a knob's: a socket's <see cref="PortSpec.Min"/> is the editor's
    /// suggestion and a saved value outside it widens the slider, where a field's
    /// range is what the value means and is held to on every path into it.
    /// </remarks>
    private static void Store(NodeInstance node, NodeExtra extra, ExtraField field, JsonNode value)
    {
        var held = extra.Stored(node.StateOf(extra.Key));
        held[field.Key] = field.Sane(value);

        node.SetState(extra.Key, held);
    }

    /// <summary>A label and a switch, laid out on the same grid a knob's row uses.</summary>
    private Control ToggleRow(string label, bool value, string because, Action<bool> store)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("78,*") };

        var caption = new TextBlock
        {
            Text = label,
            Width = 78,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var box = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };

        box.IsCheckedChanged += (_, _) =>
        {
            store(box.IsChecked == true);
            editor.NotifyPatchChanged(because);
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
    /// The name alone rather than the whole path, because a path is far wider
    /// than the panel and the last part of it is the part anybody recognises.
    /// The full one is on the tooltip, which is where it is wanted — a file that
    /// has gone is found again by knowing where it was supposed to be.
    /// <para>
    /// Nothing here says whether the file could be read. That is the compiler's
    /// to say and it says it in the status bar with everything else, naming the
    /// module: a second, quieter version of the same complaint on the panel
    /// would be one more thing to keep true.
    /// </para>
    /// </remarks>
    private Control BuildSampleRow(NodeInstance node) => BuildFileRow(
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
    /// A file this instance carries: what it is called, what it currently is,
    /// and a button that goes and finds another.
    /// </summary>
    /// <remarks>
    /// One row for both kinds, because the two differ in four strings and
    /// nothing else — the picker's title, the label, the filter, and what to do
    /// with what comes back. Written twice it would have been sixty lines said
    /// again, and the second copy is where the tooltip stops being set.
    /// </remarks>
    private Control BuildFileRow(
        string label,
        string? held,
        string title,
        FilePickerFileType kind,
        Action<string> store)
    {
        var chosen = held ?? string.Empty;

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("78,*,Auto"),
            Margin = new Thickness(0, 8, 0, 0),
        };

        var caption = new TextBlock
        {
            Text = label,
            Width = 78,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var name = new TextBlock
        {
            Text = chosen.Length == 0 ? "none chosen" : Path.GetFileName(chosen),
            FontSize = 12,
            Opacity = chosen.Length == 0 ? 0.45 : 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0),
        };

        if (chosen.Length > 0) ToolTip.SetTip(name, chosen);

        var choose = new Button { Content = "Choose…", FontSize = 11 };

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

            editor.NotifyPatchChanged();
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

    private Control BuildInputRow(NodeInstance node, PortSpec spec, int index)
    {
        var connected = editor.Patch.IncomingTo(node.Id, index) is not null;

        var label = new TextBlock
        {
            Text = spec.Name,
            Width = 78,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        // A knob whose number is not what it means gets a column for what it
        // does mean, since "57" is not what anyone means by the note they are
        // picking and "-3" is not what they mean by a millisecond. A count needs
        // no such column — the number is already what it stands for — but it
        // lands on whole numbers for the same reason a note does.
        var named = spec.Display != PortDisplay.Number;
        var whole = spec.Stepped;

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(named ? "78,*,84,40" : "78,*,84") };
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        if (connected)
        {
            var wired = new TextBlock
            {
                Text = "◀ patched",
                FontSize = 12,
                Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(wired, 1);
            Grid.SetColumnSpan(wired, named ? 3 : 2);
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
                FontSize = 12,
                Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(implied, 1);
            Grid.SetColumnSpan(implied, named ? 3 : 2);
            row.Children.Add(implied);
            return row;
        }

        var value = index < node.InputValues.Length ? node.InputValues[index] : spec.Default;

        // Named after the socket, so a slider dragged across its range is one
        // step to undo rather than one per frame of the drag.
        return ValueRow(spec.Name, spec, value, $"{node.Id} input {index}", next =>
        {
            if (index < node.InputValues.Length) node.InputValues[index] = next;
        });
    }

    /// <summary>
    /// A label, a slider, a number box, and — where the number is not what it
    /// means — what it does mean written beside them.
    /// </summary>
    /// <remarks>
    /// Shared by a socket's knob and by a plugin's declared number field, which
    /// is the point: a plugin gets snapping, formatting and the widened range for
    /// nothing, and its row reads exactly like the knob two rows above it because
    /// it is the same control. The caller says where the value lives; nothing
    /// here knows whether that is an input array or a stored object.
    /// </remarks>
    /// <param name="label">What to write in the left column.</param>
    /// <param name="spec">The range, the display and whether it snaps.</param>
    /// <param name="value">What it starts at.</param>
    /// <param name="because">
    /// What to file the edit under, so that dragging is one undo step rather than
    /// one per frame.
    /// </param>
    /// <param name="store">Where the new value goes.</param>
    private Control ValueRow(
        string label,
        PortSpec spec,
        float value,
        string because,
        Action<float> store)
    {
        // A knob whose number is not what it means gets a column for what it
        // does mean, since "57" is not what anyone means by the note they are
        // picking and "-3" is not what they mean by a millisecond. A count needs
        // no such column — the number is already what it stands for — but it
        // lands on whole numbers for the same reason a note does.
        var named = spec.Display != PortDisplay.Number;
        var whole = spec.Stepped;

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(named ? "78,*,84,40" : "78,*,84") };

        var caption = new TextBlock
        {
            Text = label,
            Width = 78,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

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
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            ShowButtonSpinner = false,
        };

        var name = new TextBlock
        {
            Text = spec.Format(value),
            FontSize = 12,
            Opacity = 0.75,
            Margin = new Thickness(6, 0, 0, 0),
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
        Grid.SetColumn(numeric, 2);
        row.Children.Add(slider);
        row.Children.Add(numeric);

        if (named)
        {
            Grid.SetColumn(name, 3);
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
