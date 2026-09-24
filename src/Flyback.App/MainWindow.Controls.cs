using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.App.Midi;
using Flyback.Core.Graph;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App;

/// <summary>
/// The knob panel: adding and turning knobs, linking sockets to them on the canvas,
/// and learning the MIDI controller one follows (ADR-0086).
/// </summary>
public sealed partial class MainWindow
{
    private readonly ControlsPanel controlsPanel = new() { IsVisible = false };

    /// <summary>The knobs over the picture while it has the window.</summary>
    private readonly StageKnobs stageKnobs = new() { IsVisible = false };

    /// <summary>The edge above the panel, dragged to give it more rows or fewer.</summary>
    private readonly GridSplitter controlsSplitter = new()
    {
        Name = "controls-splitter",
        Background = Brushes.Transparent,
        Height = 5,
        IsVisible = false,
    };

    /// <summary>The row the panel stands in, under the canvas or, swapped, under the preview.</summary>
    private RowDefinition? ControlsRow => controlsPanel.Parent is Grid grid ? grid.RowDefinitions[2] : null;

    /// <summary>
    /// The panel's height, kept while it is hidden. One row of knobs to start with;
    /// more rows wrap in beneath once it is dragged taller.
    /// </summary>
    private GridLength controlsShare = new(118);

    /// <summary>The settings window's MIDI section.</summary>
    private readonly StackPanel midiSection = new() { Spacing = 8, Width = 280 };

    /// <summary>The instruments Flyback knows by name, shipped and the user's own.</summary>
    private readonly InstrumentLibrary instruments = InstrumentLibrary.Load();

    /// <summary>Which backend hears a keyboard, and which plugin it came from.</summary>
    private readonly TextBlock midiNote = new()
    {
        Name = "midiNote",
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,
    };

    private readonly ToggleButton controlsButton =
        ToolbarButtons.Toggle("controls", "◎", "Show the knob panel, for turning the patch by hand or from a MIDI controller  (Ctrl+K)");

    private ControlHub controls = null!;

    /// <summary>Knob positions heard from hardware and not yet shown, keyed by knob.</summary>
    private readonly Dictionary<Guid, float> heard = [];

    private readonly Lock heardGate = new();

    private bool heardPosted;

    private CancellationTokenSource? learning;

    /// <summary>How many knobs the panel last showed, so a patch arriving with some opens it.</summary>
    private int knobsShown;

    private void WireControls()
    {
        controls = new ControlHub(midi);

        controls.Turned += (id, value) =>
        {
            preview.Refresh();

            lock (heardGate)
            {
                heard[id] = value;
                if (heardPosted) return;
                heardPosted = true;
            }

            Dispatcher.UIThread.Post(ShowHeard);
        };

        controlsButton.IsCheckedChanged += (_, _) => ShowControls(controlsButton.IsChecked == true);

        controlsPanel.Reading = (id, value) => StageKnobs.Reading(editor.Patch, id, value);

        controlsPanel.Label = binding => instruments.Label(binding, Source(binding.Device));
        controlsPanel.Explain = binding => instruments.Describe(binding, Source(binding.Device));

        controlsPanel.Instruments = () => midi.Sources
            .Select(source => (source, Profile: instruments.For(source)))
            .Where(pair => pair.Profile is not null)
            .Select(pair => new PanelInstrument(pair.source.Id, pair.Profile!))
            .ToList();

        controlsPanel.BindRequested += (id, binding) =>
        {
            if (editor.Patch.Control(id) is not { } control) return;

            learning?.Cancel();
            control.Midi = binding;
            editor.NotifyPatchChanged();
            document.PanelEdited();
            Report($"'{control.Name}' follows {instruments.Describe(binding, Source(binding.Device))}.");
        };

        controlsPanel.Describe = id =>
        {
            var names = ControlMap.Following(editor.Patch, id)
                .Select(f => NodeCatalog.Get(f.Node.TypeId) is { } def && f.Port < def.Inputs.Count
                    ? $"{f.Node.Title(def)} › {def.Inputs[f.Port].Name}"
                    : null)
                .OfType<string>()
                .ToList();

            return names.Count == 0 ? "Follows nothing yet. Click its name, then sockets, to link them." : string.Join(", ", names);
        };

        controlsPanel.AddRequested += () =>
        {
            var added = editor.Patch.AddControl();

            editor.NotifyPatchChanged();
            document.PanelEdited();
            Link(added.Id);
        };

        controlsPanel.Turning += TurnKnob;
        stageKnobs.Turning += TurnKnob;
        controlsPanel.TurnEnded += document.LetGoOfKnob;
        stageKnobs.TurnEnded += document.LetGoOfKnob;

        controlsPanel.LinkRequested += id => Link(editor.LinkingControl == id ? null : id);

        controlsPanel.LearnRequested += id => _ = LearnAsync(id);
        controlsPanel.LearnOnwardRequested += id => _ = LearnAsync(id, onward: true);

        controlsPanel.ForgetRequested += id =>
        {
            if (editor.Patch.Control(id) is not { } control) return;

            control.Midi = null;
            editor.NotifyPatchChanged();
            document.PanelEdited();
        };

        controlsPanel.Renamed += (id, name) =>
        {
            if (editor.Patch.Control(id) is not { } control) return;

            control.Name = name;
            editor.NotifyPatchChanged();
            document.PanelEdited();
        };

        controlsPanel.Logarithmic = id =>
        {
            var links = ControlMap.Following(editor.Patch, id).ToList();

            return links.Count == 0 ? null : links.All(f => f.Link.Knee > 0f);
        };

        controlsPanel.LogarithmicRequested += (id, inDecades) =>
        {
            if (editor.Patch.Control(id) is not { } knob) return;

            var following = ControlMap.Following(editor.Patch, id).ToList();

            foreach (var (node, port, link) in following)
            {
                if (NodeCatalog.Get(node.TypeId) is not { } def || port >= def.Inputs.Count) continue;

                var swept = link.Swept(inDecades, def.Inputs[port]);
                ControlMap.Link(node, port, swept);

                // One socket keeps reading what it read, so the switch is not heard.
                if (following.Count == 1)
                {
                    knob.Value = swept.Inverse(link.At(knob.Value));
                    controls.Set(id, knob.Value);
                }
            }

            editor.NotifyPatchChanged();
        };

        controlsPanel.MoveRequested += (id, index) =>
        {
            if (!editor.Patch.MoveControl(id, index)) return;

            editor.NotifyPatchChanged();
            document.PanelEdited();
        };

        controlsPanel.RemoveRequested += id =>
        {
            if (editor.LinkingControl == id) Link(null);
            if (controlsPanel.Learning == id) learning?.Cancel();

            if (!editor.Patch.RemoveControl(id)) return;

            editor.NotifyPatchChanged();
            document.PanelEdited();
        };

        editor.SocketPicked += (_, pick) => PickSocket(pick);

        // A socket's own knob on the canvas: heard as it turns, written into the
        // text and the panel when the hand comes off it.
        editor.InputTurned += (_, pick) => document.Turned(pick.Node, pick.Port);
        editor.InputLetGo += (_, pick) =>
        {
            document.HandCameOff();
            if (editor.SelectedNode?.Id == pick.Node || editor.SelectedGroup?.Members.Contains(pick.Node) == true) BuildInspector();
        };
    }

    /// <summary>A knob turned by hand, on the panel or over the picture.</summary>
    private void TurnKnob(Guid id, float value)
    {
        if (editor.Patch.Control(id) is not { } control) return;

        control.Value = value;
        controls.Set(id, value);
        controlsPanel.Move(id, value, heard: false);
        foreach (var stage in Stages) stage.Move(id, value);
        editor.InvalidateVisual();
        preview.Refresh();
    }

    /// <summary>Every set of knobs over a picture: the window's own, and the other monitor's while it has one.</summary>
    private IEnumerable<StageKnobs> Stages => pictureKnobs is { } away ? [stageKnobs, away] : [stageKnobs];

    /// <summary>
    /// Puts the knobs over the picture where it is full screen, they are wanted and
    /// the patch has any.
    /// </summary>
    private void SyncStageKnobs()
    {
        stageKnobs.IsVisible = previewIsFullScreen && stageKnobs.Any;

        if (pictureKnobs is { } away) away.IsVisible = away.Any;
    }

    /// <summary>The settings window's MIDI section: what a controller does to a knob that sits elsewhere.</summary>
    private void BuildMidiSection()
    {
        ToolTip.SetTip(outputSections.Takeover,
            "When a controller's knob is not where the knob on screen is: jump straight to the controller, "
            + "or leave the knob alone until the controller passes it. Flyback's own, whichever plugin "
            + "hears the controller.");

        ToolTip.SetTip(outputSections.KeyboardLayout,
            "How the computer keyboard is laid out on a patch when its first MIDI In is added: as a piano, "
            + "or as a scale, one note to a key. Patches that already have a MIDI In keep their own.");

        midiNote.Text = plugins.PreferredMidiInput is { } input
            ? OutputSections.Attributed($"Heard through {input.Name}", plugins.Provider(input))
            : "No MIDI plugin is installed, so the only instrument is the computer's own keyboard.";

        midiSection.Children.Add(midiNote);
        midiSection.Children.Add(InspectorRows.Field("Knobs", outputSections.Takeover));
        midiSection.Children.Add(InspectorRows.Field("New keyboard", outputSections.KeyboardLayout));

        var known = string.Join(", ", instruments.Profiles.Select(profile => profile.Name));
        var instrumentsNote = new TextBlock
        {
            Text = $"Known by name: {known}. A profile of your own, one .json per instrument, goes in {InstrumentLibrary.UserFolder}.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = Text.Small,
            Foreground = Text.Muted,
        };
        ToolTip.SetTip(instrumentsNote,
            "An instrument Flyback knows by name offers its tracks on a MIDI In's channel field, binds a knob "
            + "from the panel's menu without being touched, and names what a learned knob follows.");
        midiSection.Children.Add(instrumentsNote);
    }

    /// <summary>Shows or hides the panel, keeping the toolbar button in step.</summary>
    private void ShowControls(bool shown)
    {
        // The full screen preview owns every row, the knobs' too while swapped.
        if (previewIsFullScreen) return;

        // Only on a change: showing a panel already shown would put back the height
        // it had when last hidden, over whatever it has been dragged to since.
        if (ControlsRow is { } controlsRow && shown != controlsPanel.IsVisible)
        {
            // A pixel row rather than an auto one, so the splitter has a height to
            // change; zeroed while hidden, with its minimum, the way the assistant's is.
            if (!shown && controlsPanel.IsVisible) controlsShare = controlsRow.Height;

            controlsRow.MinHeight = shown ? 60d : 0d;
            controlsRow.Height = shown ? controlsShare : new GridLength(0);
        }

        controlsPanel.IsVisible = shown;
        controlsSplitter.IsVisible = shown;

        if (controlsButton.IsChecked != shown) controlsButton.IsChecked = shown;

        if (!shown) Link(null);
    }

    /// <summary>Redraws the panel from the patch. Called after every recompile.</summary>
    private void RefreshControls()
    {
        var knobs = editor.Patch.Controls ?? [];

        controls.Follow(editor.Patch, preview.Live, audio.Live);
        controlsPanel.Show(knobs);
        foreach (var stage in Stages) stage.Show(editor.Patch);
        SyncStageKnobs();

        if (knobs.Count > 0 && knobsShown == 0) ShowControls(true);
        knobsShown = knobs.Count;

        if (editor.LinkingControl is { } linking && editor.Patch.Control(linking) is null) Link(null);
    }

    /// <summary>Starts linking sockets to a knob, or stops with null.</summary>
    private void Link(Guid? control)
    {
        editor.LinkingControl = control;
        controlsPanel.Linking = control;

        if (control is { } id && editor.Patch.Control(id) is { } knob)
        {
            ShowControls(true);
            Report($"Click sockets on the canvas to link them to '{knob.Name}', or a linked one to let it go. Esc when done.");
        }
    }

    /// <summary>Links a clicked socket to the knob being linked, or lets it go if it already follows it.</summary>
    private void PickSocket(SocketPick pick)
    {
        if (editor.LinkingControl is not { } id || editor.Patch.Control(id) is not { } knob) return;
        if (editor.Patch.Find(pick.Node) is not { } node || NodeCatalog.Get(node.TypeId) is not { } def) return;
        if (pick.Port >= def.Inputs.Count) return;

        var spec = def.Inputs[pick.Port];
        var socket = $"{node.Title(def)} › {spec.Name}";

        if (editor.Patch.IncomingTo(node.Id, pick.Port) is not null || !NodeEditor.Linkable(spec))
        {
            Report($"'{socket}' cannot follow a knob: only a socket resting on its own knob can.");
            return;
        }

        if (ControlMap.Of(node, pick.Port) is { } existing && existing.Control == id)
        {
            if (pick.Port < node.InputValues.Length) node.InputValues[pick.Port] = existing.At(knob.Value);

            ControlMap.Unlink(node, pick.Port);
            editor.NotifyPatchChanged();
            Report($"'{socket}' no longer follows '{knob.Name}'.");
            return;
        }

        var resting = pick.Port < node.InputValues.Length ? node.InputValues[pick.Port] : spec.Default;
        var link = ControlLink.For(id, spec, resting);

        // A knob linked for the first time is turned to where the socket already is,
        // so linking changes nothing that is heard or seen.
        if (!ControlMap.Following(editor.Patch, id).Any())
        {
            knob.Value = link.Inverse(resting);
            controls.Set(id, knob.Value);
        }

        ControlMap.Link(node, pick.Port, link);
        editor.NotifyPatchChanged();
        Report($"'{socket}' follows '{knob.Name}' from {spec.Format(link.Min)} to {spec.Format(link.Max)}. Esc when done.");
    }

    /// <summary>
    /// Waits for a controller to move and binds the knob to it, and
    /// <paramref name="onward"/> goes on to the next knob and the next until the
    /// panel ends or Escape stops it. The controller just learned is not taken
    /// again for the knob after, since the hand that turned it is still on it.
    /// </summary>
    private async Task LearnAsync(Guid id, bool onward = false)
    {
        if (editor.Patch.Control(id) is null) return;

        learning?.Cancel();

        var devices = midi.Sources.Select(s => s.Id).Where(s => s != MidiSources.Keyboard).ToList();

        if (devices.Count == 0)
        {
            Report("No MIDI device is plugged in, so there is no controller to learn.");
            return;
        }

        var knobs = editor.Patch.Controls ?? [];
        var from = knobs.FindIndex(knob => knob.Id == id);
        var run = onward ? knobs.Skip(from).Select(knob => knob.Id).ToList() : [id];

        // Only once there is something to wait for: the field is cleared by the
        // finally below, and a source left in it after this method has disposed it
        // throws the next time Escape cancels it.
        using var cancel = learning = new CancellationTokenSource();
        var showing = id;

        ShowControls(true);

        try
        {
            MidiBinding? last = null;

            for (var i = 0; i < run.Count; i++)
            {
                if (editor.Patch.Control(run[i]) is not { } knob) continue;

                showing = knob.Id;
                controlsPanel.Learning = knob.Id;
                Report(run.Count == 1
                    ? $"Move a knob or fader on your controller for '{knob.Name}'. Esc to stop."
                    : $"Move a knob or fader on your controller for '{knob.Name}' ({i + 1} of {run.Count}). Esc to stop.");

                var heard = await controls.LearnAsync(devices, cancel.Token, last);

                if (heard is null || editor.Patch.Control(knob.Id) is not { } still) return;

                var source = Source(heard.Device);
                var binding = Settled(heard, source);

                still.Midi = binding;
                editor.NotifyPatchChanged();
                document.PanelEdited();
                last = heard;

                Report(instruments.For(source ?? default) is not null
                    ? $"'{still.Name}' follows {instruments.Describe(binding, source)}."
                    : $"'{still.Name}' follows {binding.Label} on {source?.Name ?? binding.Device}.");
            }
        }
        finally
        {
            // Only where this is still the learn under way. One that gave way to
            // another ends after the other has begun, and the other may be for
            // this same knob — whose "move a controller" it would be wiping.
            if (learning == cancel)
            {
                learning = null;

                if (controlsPanel.Learning == showing) controlsPanel.Learning = null;
            }
        }
    }

    /// <summary>
    /// The binding a learn stores: on the channel the controller moved on where
    /// the instrument keeps a track per channel, since the same knob on every
    /// track sends the same number, and on any channel otherwise, so a
    /// controller moved to another channel goes on turning the knob.
    /// </summary>
    private MidiBinding Settled(MidiBinding heard, MidiSource? source) =>
        instruments.For(source ?? default) is { Tracks.Count: > 0 } ? heard : heard with { Channel = 0 };

    /// <summary>The instrument with this id as it is plugged in now, or null while it is not.</summary>
    private MidiSource? Source(string id) =>
        midi.Sources.FirstOrDefault(s => s.Id == id) is { Id: not null } source ? source : null;

    /// <summary>Stops linking and learning, and says whether there was either to stop.</summary>
    private bool StopControlModes()
    {
        var stopped = editor.LinkingControl is not null || learning is not null;

        learning?.Cancel();
        Link(null);

        return stopped;
    }

    /// <summary>Shows what hardware has done to the knobs since this last ran. On the UI thread.</summary>
    private void ShowHeard()
    {
        Dictionary<Guid, float> moved;

        lock (heardGate)
        {
            moved = new(heard);
            heard.Clear();
            heardPosted = false;
        }

        foreach (var (id, value) in moved)
        {
            if (editor.Patch.Control(id) is not { } control) continue;

            control.Value = value;
            controlsPanel.Move(id, value, heard: true);
            foreach (var stage in Stages) stage.Move(id, value);
        }

        editor.InvalidateVisual();
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
            editor.NotifyPatchChanged();
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

                editor.NotifyPatchChanged($"{node.Id} range {index}");
            };

            return Boxed.NeverBlank(box);
        }
    }
}
