using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.Core.Graph;

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
            Background = new SolidColorBrush(Colours.Panel),
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
    /// Rebuilt whenever selection changes. The canvas handles patching; exact
    /// numbers are easier to set with real controls than by dragging on a knob.
    /// </summary>
    private void BuildInspector()
    {
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
                     + "Select a module to edit its values.\n\n"
                     + "Select the Output for the preview size, the renderer, "
                     + "sound, and saving a frame or a clip.\n\n"
                     + "Drag from a socket to patch it into another, or onto bare "
                     + "canvas to add a module already plugged in.\n"
                     + "Drag a connected input to unplug it.\n"
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

        inspector.Children.Add(new TextBlock
        {
            Text = def.Name,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        });

        inspector.Children.Add(new TextBlock
        {
            Text = def.Category,
            FontSize = 11,
            Foreground = new SolidColorBrush(Colours.Accent(def.Category)),
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
