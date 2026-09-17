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

    /// <summary>The settings window's MIDI section.</summary>
    private readonly StackPanel midiSection = new() { Spacing = 8, Width = 280 };

    private readonly ComboBox takeover = new Picker
    {
        Name = "takeover",
        ItemsSource = new[] { "Jump to the controller", "Pick up the knob" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly ToggleButton controlsButton =
        Toggle("controls", "◎", "Show the knob panel, for turning the patch by hand or from a MIDI controller  (Ctrl+K)");

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

        controlsPanel.Reading = (id, value) =>
        {
            var following = ControlMap.Following(editor.Patch, id).Take(2).ToList();

            return following is [var (node, port, link)] && NodeCatalog.Get(node.TypeId) is { } def && port < def.Inputs.Count
                ? def.Inputs[port].Format(link.At(value))
                : null;
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
            Link(added.Id);
        };

        controlsPanel.Turning += (id, value) =>
        {
            if (editor.Patch.Control(id) is not { } control) return;

            control.Value = value;
            controls.Set(id, value);
            controlsPanel.Move(id, value, heard: false);
            editor.InvalidateVisual();
            preview.Refresh();
        };

        controlsPanel.LinkRequested += id => Link(editor.LinkingControl == id ? null : id);

        controlsPanel.LearnRequested += id => _ = LearnAsync(id);

        controlsPanel.ForgetRequested += id =>
        {
            if (editor.Patch.Control(id) is not { } control) return;

            control.Midi = null;
            editor.NotifyPatchChanged();
        };

        controlsPanel.Renamed += (id, name) =>
        {
            if (editor.Patch.Control(id) is not { } control) return;

            control.Name = name;
            editor.NotifyPatchChanged();
        };

        controlsPanel.RemoveRequested += id =>
        {
            if (editor.LinkingControl == id) Link(null);
            if (controlsPanel.Learning == id) learning?.Cancel();

            if (editor.Patch.RemoveControl(id)) editor.NotifyPatchChanged();
        };

        editor.SocketPicked += (_, pick) => PickSocket(pick);
    }

    /// <summary>The settings window's MIDI section: what a controller does to a knob that sits elsewhere.</summary>
    private void BuildMidiSection()
    {
        ToolTip.SetTip(takeover,
            "When a controller's knob is not where the knob on screen is: jump straight to the controller, "
            + "or leave the knob alone until the controller passes it.");

        midiSection.Children.Add(Field("Knobs", takeover));
    }

    /// <summary>
    /// Hands the knobs of a patch to the one built from its text, with the links of
    /// every module the text kept. The text has no way to write either.
    /// </summary>
    private static void CarryControls(Patch from, Patch to)
    {
        if (from.Controls is null || to.Controls is not null) return;

        to.Controls = [.. from.Controls.Select(c => c.Clone())];

        foreach (var node in to.Nodes)
        {
            if (from.Find(node.Id) is not { } was || node.StateOf(ControlMap.StateKey) is not null) continue;

            foreach (var (port, link) in ControlMap.All(was))
                if (port < node.InputValues.Length)
                    ControlMap.Link(node, port, link);
        }
    }

    /// <summary>Shows or hides the panel, keeping the toolbar button in step.</summary>
    private void ShowControls(bool shown)
    {
        controlsPanel.IsVisible = shown;

        if (controlsButton.IsChecked != shown) controlsButton.IsChecked = shown;

        if (!shown) Link(null);
    }

    /// <summary>Redraws the panel from the patch. Called after every recompile.</summary>
    private void RefreshControls()
    {
        var knobs = editor.Patch.Controls ?? [];

        controls.Follow(editor.Patch, preview.Live, audio.Live);
        controlsPanel.Show(knobs);

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
        var link = new ControlLink(id, Math.Min(spec.Min, resting), Math.Max(spec.Max, resting));

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

    /// <summary>Waits for a controller to move, and binds the knob to it.</summary>
    private async Task LearnAsync(Guid id)
    {
        if (editor.Patch.Control(id) is not { } knob) return;

        learning?.Cancel();
        using var cancel = learning = new CancellationTokenSource();

        var devices = midi.Sources.Select(s => s.Id).Where(s => s != MidiSources.Keyboard).ToList();

        if (devices.Count == 0)
        {
            Report("No MIDI device is plugged in, so there is no controller to learn.");
            return;
        }

        controlsPanel.Learning = id;
        ShowControls(true);
        Report($"Move a knob or fader on your controller for '{knob.Name}'. Esc to stop.");

        try
        {
            var binding = await controls.LearnAsync(devices, cancel.Token);

            if (binding is null || editor.Patch.Control(id) is not { } still) return;

            still.Midi = binding;
            editor.NotifyPatchChanged();

            var device = midi.Sources.FirstOrDefault(s => s.Id == binding.Device).Name ?? binding.Device;
            Report($"'{still.Name}' follows {binding.Label} on {device}.");
        }
        finally
        {
            if (learning == cancel) learning = null;
            if (controlsPanel.Learning == id) controlsPanel.Learning = null;
        }
    }

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
        }

        editor.InvalidateVisual();
    }

    /// <summary>
    /// A socket that follows a knob, in the inspector: which knob, the range it
    /// follows it over, and a button to let it go.
    /// </summary>
    private Control LinkedRow(NodeInstance node, PortSpec spec, int index, ControlLink link, PatchControl knob)
    {
        var row = Row("*,58,14,58,26");

        var caption = Caption(spec.Name);
        Grid.SetColumn(caption, 0);
        row.Children.Add(caption);

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
                Value = (decimal)value,
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
                editor.NotifyPatchChanged($"{node.Id} range {index}");
            };

            return box;
        }
    }
}
