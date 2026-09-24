using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Threading;
using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.App.Midi;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>
/// The panel knobs: adding and turning them, linking sockets to them on the canvas,
/// and learning the MIDI controller one follows (ADR-0086, ADR-0148).
/// </summary>
/// <remarks>
/// The knob panel is shown and hidden by the window, which owns the row it stands
/// in; this asks for it with <see cref="Wanted"/> when a knob needs to be seen.
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "learning only borrows the source LearnAsync disposes.")]
internal sealed class PanelKnobs
{
    private readonly NodeEditor editor;
    private readonly Document document;
    private readonly PreviewHost preview;
    private readonly AudioEngine audio;
    private readonly MidiHub midi;
    private readonly ReportLine report;

    /// <summary>Knob positions heard from hardware and not yet shown, keyed by knob.</summary>
    private readonly Dictionary<Guid, float> heard = [];

    private readonly Lock heardGate = new();

    private bool heardPosted;

    private CancellationTokenSource? learning;

    /// <summary>How many knobs the panel last showed, so a patch arriving with some opens it.</summary>
    private int knobsShown;

    /// <summary>The knob panel itself.</summary>
    public ControlsPanel View { get; } = new() { IsVisible = false };

    /// <summary>The knobs over the picture while it has the window.</summary>
    public StageKnobs Stage { get; } = new() { IsVisible = false };

    /// <summary>The knobs over the picture on another monitor, while it is there.</summary>
    public StageKnobs? Away { get; set; }

    /// <summary>What turns the program's live values, from the panel and from hardware.</summary>
    public ControlHub Hub { get; }

    /// <summary>The instruments Flyback knows by name, shipped and the user's own.</summary>
    public InstrumentLibrary Instruments { get; } = InstrumentLibrary.Load();

    /// <summary>The settings window's MIDI section.</summary>
    public StackPanel MidiSection { get; } = new() { Spacing = 8, Width = 280 };

    /// <summary>Whether the picture has the window, where the knobs over it are shown.</summary>
    public bool OverPicture { get; set; }

    /// <summary>The knob panel should be shown: a knob was added, is being linked or learned.</summary>
    public event EventHandler? Wanted;

    public PanelKnobs(Shell shell, PreviewHost preview, AudioEngine audio, MidiHub midi)
    {
        editor = shell.Editor;
        document = shell.Document;
        this.preview = preview;
        this.audio = audio;
        this.midi = midi;
        report = shell.Report;

        Hub = new ControlHub(midi);

        Hub.Turned += (id, value) =>
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

        View.Reading = (id, value) => StageKnobs.Reading(editor.Patch, id, value);

        View.Label = binding => Instruments.Label(binding, Source(binding.Device));
        View.Explain = binding => Instruments.Describe(binding, Source(binding.Device));

        View.Instruments = () => midi.Sources
            .Select(source => (source, Profile: Instruments.For(source)))
            .Where(pair => pair.Profile is not null)
            .Select(pair => new PanelInstrument(pair.source.Id, pair.Profile!))
            .ToList();

        View.BindRequested += (id, binding) =>
        {
            if (editor.Patch.Control(id) is not { } control) return;

            learning?.Cancel();
            control.Midi = binding;
            editor.NotifyPatchChanged();
            document.PanelEdited();
            report.Say($"'{control.Name}' follows {Instruments.Describe(binding, Source(binding.Device))}.");
        };

        View.Describe = id =>
        {
            var names = ControlMap.Following(editor.Patch, id)
                .Select(f => NodeCatalog.Get(f.Node.TypeId) is { } def && f.Port < def.Inputs.Count
                    ? $"{f.Node.Title(def)} › {def.Inputs[f.Port].Name}"
                    : null)
                .OfType<string>()
                .ToList();

            return names.Count == 0 ? "Follows nothing yet. Click its name, then sockets, to link them." : string.Join(", ", names);
        };

        View.AddRequested += () =>
        {
            var added = editor.Patch.AddControl();

            editor.NotifyPatchChanged();
            document.PanelEdited();
            Link(added.Id);
        };

        View.Turning += Turn;
        Stage.Turning += Turn;
        View.TurnEnded += document.LetGoOfKnob;
        Stage.TurnEnded += document.LetGoOfKnob;

        View.LinkRequested += id => Link(editor.LinkingControl == id ? null : id);

        View.LearnRequested += id => _ = LearnAsync(id);
        View.LearnOnwardRequested += id => _ = LearnAsync(id, onward: true);

        View.ForgetRequested += id =>
        {
            if (editor.Patch.Control(id) is not { } control) return;

            control.Midi = null;
            editor.NotifyPatchChanged();
            document.PanelEdited();
        };

        View.Renamed += (id, name) =>
        {
            if (editor.Patch.Control(id) is not { } control) return;

            control.Name = name;
            editor.NotifyPatchChanged();
            document.PanelEdited();
        };

        View.Logarithmic = id =>
        {
            var links = ControlMap.Following(editor.Patch, id).ToList();

            return links.Count == 0 ? null : links.All(f => f.Link.Knee > 0f);
        };

        View.LogarithmicRequested += (id, inDecades) =>
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
                    Hub.Set(id, knob.Value);
                }
            }

            editor.NotifyPatchChanged();
        };

        View.MoveRequested += (id, index) =>
        {
            if (!editor.Patch.MoveControl(id, index)) return;

            editor.NotifyPatchChanged();
            document.PanelEdited();
        };

        View.RemoveRequested += id =>
        {
            if (editor.LinkingControl == id) Link(null);
            if (View.Learning == id) learning?.Cancel();

            if (!editor.Patch.RemoveControl(id)) return;

            editor.NotifyPatchChanged();
            document.PanelEdited();
        };

        editor.SocketPicked += (_, pick) => PickSocket(pick);
    }

    /// <summary>A knob turned by hand, on the panel or over the picture.</summary>
    public void Turn(Guid id, float value)
    {
        if (editor.Patch.Control(id) is not { } control) return;

        control.Value = value;
        Hub.Set(id, value);
        View.Move(id, value, heard: false);
        foreach (var stage in Stages) stage.Move(id, value);
        editor.InvalidateVisual();
        preview.Refresh();
    }

    /// <summary>Every set of knobs over a picture: the window's own, and the other monitor's while it has one.</summary>
    private IEnumerable<StageKnobs> Stages => Away is { } away ? [Stage, away] : [Stage];

    /// <summary>
    /// Puts the knobs over the picture where it is full screen, they are wanted and
    /// the patch has any.
    /// </summary>
    public void SyncStages()
    {
        Stage.IsVisible = OverPicture && Stage.Any;

        if (Away is { } away) away.IsVisible = away.Any;
    }

    /// <summary>The settings window's MIDI section: what a controller does to a knob that sits elsewhere.</summary>
    /// <param name="takeover">How a knob meets a controller that disagrees with it.</param>
    /// <param name="keyboardLayout">How a new patch lays out the computer's keyboard.</param>
    public void BuildMidiSection(PluginCatalog plugins, ComboBox takeover, ComboBox keyboardLayout)
    {
        ToolTip.SetTip(takeover,
            "When a controller's knob is not where the knob on screen is: jump straight to the controller, "
            + "or leave the knob alone until the controller passes it. Flyback's own, whichever plugin "
            + "hears the controller.");

        ToolTip.SetTip(keyboardLayout,
            "How the computer keyboard is laid out on a patch when its first MIDI In is added: as a piano, "
            + "or as a scale, one note to a key. Patches that already have a MIDI In keep their own.");

        // Which backend hears a keyboard, and which plugin it came from.
        var midiNote = new TextBlock
        {
            Name = "midiNote",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = plugins.PreferredMidiInput is { } input
                ? OutputSections.Attributed($"Heard through {input.Name}", plugins.Provider(input))
                : "No MIDI plugin is installed, so the only instrument is the computer's own keyboard.",
        };

        MidiSection.Children.Add(midiNote);
        MidiSection.Children.Add(InspectorRows.Field("Knobs", takeover));
        MidiSection.Children.Add(InspectorRows.Field("New keyboard", keyboardLayout));

        var known = string.Join(", ", Instruments.Profiles.Select(profile => profile.Name));
        var instrumentsNote = new TextBlock
        {
            Text = $"Known by name: {known}. A profile of your own, one .json per instrument, goes in {InstrumentLibrary.UserFolder}.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = Text.Small,
            Foreground = Text.Muted,
        };
        ToolTip.SetTip(instrumentsNote,
            "An instrument Flyback knows by name offers its tracks on a MIDI In's channel field, binds a knob "
            + "from the panel's menu without being touched, and names what a learned knob follows.");
        MidiSection.Children.Add(instrumentsNote);
    }

    /// <summary>Redraws the panel from the patch. Called after every recompile.</summary>
    public void Refresh()
    {
        var knobs = editor.Patch.Controls ?? [];

        Hub.Follow(editor.Patch, preview.Live, audio.Live);
        View.Show(knobs);
        foreach (var stage in Stages) stage.Show(editor.Patch);
        SyncStages();

        if (knobs.Count > 0 && knobsShown == 0) Wanted?.Invoke(this, EventArgs.Empty);
        knobsShown = knobs.Count;

        if (editor.LinkingControl is { } linking && editor.Patch.Control(linking) is null) Link(null);
    }

    /// <summary>Starts linking sockets to a knob, or stops with null.</summary>
    public void Link(Guid? control)
    {
        editor.LinkingControl = control;
        View.Linking = control;

        if (control is { } id && editor.Patch.Control(id) is { } knob)
        {
            Wanted?.Invoke(this, EventArgs.Empty);
            report.Say($"Click sockets on the canvas to link them to '{knob.Name}', or a linked one to let it go. Esc when done.");
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
            report.Say($"'{socket}' cannot follow a knob: only a socket resting on its own knob can.");
            return;
        }

        if (ControlMap.Of(node, pick.Port) is { } existing && existing.Control == id)
        {
            if (pick.Port < node.InputValues.Length) node.InputValues[pick.Port] = existing.At(knob.Value);

            ControlMap.Unlink(node, pick.Port);
            editor.NotifyPatchChanged();
            report.Say($"'{socket}' no longer follows '{knob.Name}'.");
            return;
        }

        var resting = pick.Port < node.InputValues.Length ? node.InputValues[pick.Port] : spec.Default;
        var link = ControlLink.For(id, spec, resting);

        // A knob linked for the first time is turned to where the socket already is,
        // so linking changes nothing that is heard or seen.
        if (!ControlMap.Following(editor.Patch, id).Any())
        {
            knob.Value = link.Inverse(resting);
            Hub.Set(id, knob.Value);
        }

        ControlMap.Link(node, pick.Port, link);
        editor.NotifyPatchChanged();
        report.Say($"'{socket}' follows '{knob.Name}' from {spec.Format(link.Min)} to {spec.Format(link.Max)}. Esc when done.");
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
            report.Say("No MIDI device is plugged in, so there is no controller to learn.");
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

        Wanted?.Invoke(this, EventArgs.Empty);

        try
        {
            MidiBinding? last = null;

            for (var i = 0; i < run.Count; i++)
            {
                if (editor.Patch.Control(run[i]) is not { } knob) continue;

                showing = knob.Id;
                View.Learning = knob.Id;
                report.Say(run.Count == 1
                    ? $"Move a knob or fader on your controller for '{knob.Name}'. Esc to stop."
                    : $"Move a knob or fader on your controller for '{knob.Name}' ({i + 1} of {run.Count}). Esc to stop.");

                var moved = await Hub.LearnAsync(devices, cancel.Token, last);

                if (moved is null || editor.Patch.Control(knob.Id) is not { } still) return;

                var source = Source(moved.Device);
                var binding = Settled(moved, source);

                still.Midi = binding;
                editor.NotifyPatchChanged();
                document.PanelEdited();
                last = moved;

                report.Say(Instruments.For(source ?? default) is not null
                    ? $"'{still.Name}' follows {Instruments.Describe(binding, source)}."
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

                if (View.Learning == showing) View.Learning = null;
            }
        }
    }

    /// <summary>
    /// The binding a learn stores: on the channel the controller moved on where
    /// the instrument keeps a track per channel, since the same knob on every
    /// track sends the same number, and on any channel otherwise, so a
    /// controller moved to another channel goes on turning the knob.
    /// </summary>
    private MidiBinding Settled(MidiBinding moved, MidiSource? source) =>
        Instruments.For(source ?? default) is { Tracks.Count: > 0 } ? moved : moved with { Channel = 0 };

    /// <summary>The instrument with this id as it is plugged in now, or null while it is not.</summary>
    private MidiSource? Source(string id) =>
        midi.Sources.FirstOrDefault(s => s.Id == id) is { Id: not null } source ? source : null;

    /// <summary>Stops linking and learning, and says whether there was either to stop.</summary>
    public bool StopModes()
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
            View.Move(id, value, heard: true);
            foreach (var stage in Stages) stage.Move(id, value);
        }

        editor.InvalidateVisual();
    }
}
