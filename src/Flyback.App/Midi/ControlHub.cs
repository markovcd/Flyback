using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Midi;

namespace Flyback.App.Midi;

/// <summary>What a hardware controller does to a knob that sits somewhere else.</summary>
public enum Takeover
{
    /// <summary>The knob jumps to wherever the controller is.</summary>
    Jump,

    /// <summary>The controller is ignored until it passes where the knob is.</summary>
    PickUp,
}

/// <summary>
/// Where the panel's knobs are right now, and the join between turning one — on
/// screen or on a controller — and the programs reading it.
/// </summary>
/// <remarks>
/// Like <see cref="MidiHub"/>, values are pushed into the running programs' live
/// blocks and nothing is recompiled. Turns arrive on the UI thread from the panel
/// and on the driver's thread from hardware; state is guarded by <see cref="gate"/>
/// and <see cref="Turned"/> is raised outside it.
/// </remarks>
internal sealed class ControlHub
{
    /// <summary>How far a controller has to move before learning takes it, so jitter is not learned.</summary>
    private const float LearnDistance = 2f / 127f;

    private readonly MidiHub midi;

    private readonly Lock gate = new();

    private LiveValues[] following = [];

    /// <summary>Every knob's position, keyed by id.</summary>
    private readonly Dictionary<Guid, float> values = [];

    /// <summary>
    /// Where each knob that has left the patch was when it went, for one that
    /// comes back — see <see cref="Follow"/>.
    /// </summary>
    private readonly Dictionary<Guid, float> gone = [];

    private (MidiBinding Binding, Guid Control)[] bindings = [];

    /// <summary>The knobs a controller has caught up with, under <see cref="Takeover.PickUp"/>.</summary>
    private readonly HashSet<Guid> caught = [];

    /// <summary>Where each binding's controller last sat, for telling that it passed the knob.</summary>
    private readonly Dictionary<Guid, float> lastHeard = [];

    private string[] bound = [];

    private Pending? learning;

    public ControlHub(MidiHub midi)
    {
        this.midi = midi;
        midi.Controlled += Receive;
    }

    public Takeover Takeover { get; set; }

    /// <summary>
    /// A knob moved because a controller did: its id and where it now sits. Raised on
    /// the driver's thread.
    /// </summary>
    public event Action<Guid, float>? Turned;

    /// <summary>Whether a knob is waiting for a controller to move.</summary>
    public bool Learning
    {
        get
        {
            lock (gate) return learning is not null;
        }
    }

    /// <summary>
    /// Points this at the programs now running and the patch they were compiled
    /// from, seeds their blocks with where every knob is, and holds open the devices
    /// the knobs are bound to. Call on the UI thread after every recompile.
    /// </summary>
    /// <remarks>
    /// A knob this already knows stays where it was turned to, and the patch is told:
    /// an undo is an edit, and where a performer's hand left a knob is not.
    /// </remarks>
    public void Follow(Patch patch, params LiveValues[] blocks)
    {
        var controls = patch.Controls ?? [];

        lock (gate)
        {
            following = blocks;

            var known = new Dictionary<Guid, float>(values);
            values.Clear();

            foreach (var control in controls)
            {
                if (known.TryGetValue(control.Id, out var turned) || gone.Remove(control.Id, out turned))
                    control.Value = turned;

                values[control.Id] = control.Value;
                foreach (var block in blocks) block.Set(control.Key, control.Value);
            }

            // A knob that has left the patch may be on its way back: removing one
            // is an edit, and so is the undo that returns it. The snapshot it
            // returns from holds it where the last edit found it, which is not
            // where the hand left it.
            foreach (var (id, at) in known)
                if (!values.ContainsKey(id))
                    gone[id] = at;

            caught.RemoveWhere(id => !values.ContainsKey(id));

            bindings = [.. controls.Where(c => c.Midi is not null).Select(c => (c.Midi!, c.Id))];
            bound = [.. bindings.Select(b => b.Binding.Device).Distinct(StringComparer.Ordinal)];
        }

        Hold();
    }

    /// <summary>
    /// Forgets where every knob was turned to, for a different document arriving.
    /// </summary>
    /// <remarks>
    /// Two files share their knobs' ids whenever one began as a copy of the other,
    /// and the one being opened says where its own knobs are.
    /// </remarks>
    public void Forget()
    {
        lock (gate)
        {
            values.Clear();
            gone.Clear();
            caught.Clear();
            lastHeard.Clear();
        }
    }

    /// <summary>
    /// Turns a knob from the screen. A controller bound to it has to catch up again
    /// before it takes over, under <see cref="Takeover.PickUp"/>.
    /// </summary>
    public void Set(Guid control, float value)
    {
        var at = Math.Clamp(value, 0f, 1f);

        lock (gate)
        {
            if (!values.ContainsKey(control)) return;

            values[control] = at;
            caught.Remove(control);

            foreach (var block in following) block.Set(PatchControl.KeyOf(control), at);
        }
    }

    /// <summary>
    /// Waits for the first controller to move on any device, and hands back a binding
    /// for it — or null if <paramref name="cancel"/> fires first. Every device is held
    /// open meanwhile.
    /// </summary>
    public async Task<MidiBinding?> LearnAsync(IEnumerable<string> devices, CancellationToken cancel)
    {
        var waiting = new Pending();

        lock (gate)
        {
            learning?.Done.TrySetResult(null);
            learning = waiting;
        }

        midi.Hold([.. devices]);

        await using (cancel.Register(() => waiting.Done.TrySetResult(null)))
        {
            try
            {
                return await waiting.Done.Task.ConfigureAwait(true);
            }
            finally
            {
                bool last;

                lock (gate)
                {
                    last = learning == waiting;
                    if (last) learning = null;
                }

                // Only the learn nothing has taken over from gives the devices
                // back. One that gave way to another ends after the other has
                // opened them for itself, and closing them now would leave that
                // one waiting on ports that are shut.
                if (last) Hold();
            }
        }
    }

    private void Hold()
    {
        string[] devices;

        lock (gate)
        {
            // A learn has every device open and gives them back when it ends,
            // to whatever is bound by then. A recompile in the middle of one
            // must not shut them under it.
            if (learning is not null) return;

            devices = bound;
        }

        midi.Hold(devices);
    }

    /// <summary>A controller moved. On the driver's thread.</summary>
    private void Receive(string device, MidiMessage message)
    {
        var turned = new List<(Guid, float)>();

        lock (gate)
        {
            if (learning is { } waiting)
            {
                var key = (device, message.Note);

                if (!waiting.First.TryGetValue(key, out var first))
                    waiting.First[key] = message.Velocity;
                else if (MathF.Abs(message.Velocity - first) >= LearnDistance)
                    waiting.Done.TrySetResult(new MidiBinding(device, 0, message.Note));

                return;
            }

            foreach (var (binding, control) in bindings)
            {
                if (!binding.Hears(device, message.Channel, message.Note)) continue;
                if (!values.TryGetValue(control, out var at)) continue;

                var reading = message.Velocity;
                var before = lastHeard.TryGetValue(control, out var heard) ? heard : (float?)null;
                lastHeard[control] = reading;

                if (Takeover == Takeover.PickUp && !caught.Contains(control))
                {
                    var close = MathF.Abs(reading - at) <= 1f / 127f;
                    var passed = before is { } b && MathF.Sign(b - at) != MathF.Sign(reading - at);

                    if (!close && !passed) continue;

                    caught.Add(control);
                }

                values[control] = reading;

                foreach (var block in following) block.Set(PatchControl.KeyOf(control), reading);

                turned.Add((control, reading));
            }
        }

        foreach (var (control, reading) in turned) Turned?.Invoke(control, reading);
    }

    private sealed class Pending
    {
        public TaskCompletionSource<MidiBinding?> Done { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Where each controller was first heard while learning.</summary>
        public Dictionary<(string Device, int Controller), float> First { get; } = [];
    }
}
