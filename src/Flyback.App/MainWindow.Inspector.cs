using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
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
            gpuButton.IsChecked = preview.Backend == PreviewBackend.Gpu;
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
                // important: the list used to stand open down the left of the
                // window, and now nothing on screen says where it went. The
                // Output is named next for the same reason — everything that
                // used to be along the top is behind it, and a setting nobody
                // can find is worse than one in the wrong place.
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
                     + "Delete removes what is selected, F frames the patch.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.5,
                FontSize = 12,
            });
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

        // A sequencer's tune is a list rather than a row of knobs (ADR-0038),
        // so it is edited as one — added to, taken from and reordered.
        if (def.DefaultSteps is not null)
            inspector.Children.Add(new StepList(node, def, because => editor.NotifyPatchChanged(because)).View);

        if (def.Inputs.Count == 0 && def.DefaultSteps is null)
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

        // Delete takes the whole selection, the same as the key does, so the
        // label counts it. Sinks are left out of the count because the graph
        // refuses them: a button offering to delete three when it can only
        // manage two would be lying about what pressing it does.
        var going = editor.SelectedNodes.Count(n => !NodeCatalog.IsSink(n.TypeId));

        var delete = new Button
        {
            Content = going > 1 ? $"Delete {going} modules" : "Delete module",
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        delete.Click += (_, _) => editor.DeleteSelected();
        inspector.Children.Add(delete);
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
    private void BeginRename(NodeInstance node, NodeDef def, Control title)
    {
        var at = inspector.Children.IndexOf(title);
        if (at < 0) return;

        var box = new TextBox
        {
            // The name it has, not the one it shows. Opening this on a module
            // nobody has renamed leaves an empty box, because empty is what it
            // means — and the definition's name is in the watermark, where it
            // reads as the thing you would get back rather than as text to
            // delete before typing.
            Text = node.Name ?? string.Empty,
            PlaceholderText = def.Name,
            MaxLength = NodeInstance.NameLimit,
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

            var before = node.Name;
            if (keep) node.Rename(def, box.Text);

            var where = inspector.Children.IndexOf(box);
            if (where >= 0) inspector.Children[where] = BuildTitle(node, def);

            // Only where it is actually a rename: the canvas draws its headers
            // from the same name and this is what redraws them, and a step in
            // the history for opening a box and closing it again would be one
            // press of undo that puts nothing back.
            if (node.Name != before) editor.NotifyPatchChanged();
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
            if (index < node.InputValues.Length) node.InputValues[index] = next;
            slider.Value = next;
            numeric.Value = (decimal)next;
            name.Text = spec.Format(next);
            updating = false;

            // Named after the socket, so a slider dragged across its range is one
            // step to undo rather than one per frame of the drag.
            editor.NotifyPatchChanged($"{node.Id} input {index}");
        }
    }
}
