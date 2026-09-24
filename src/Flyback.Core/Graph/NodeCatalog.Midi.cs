using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    /// <summary>
    /// The module that is played rather than programmed. Named here because the
    /// shell has to ask whether a patch holds one — a keyboard nothing is
    /// listening to should not be swallowing keystrokes.
    /// </summary>
    public const string MidiTypeId = "midi.in";

    /// <summary>The module that keeps a patch to an instrument's clock.</summary>
    public const string ClockTypeId = "midi.clock";

    /// <summary>
    /// How long a Clock In takes to smooth out a tick that landed early or late:
    /// the slip it absorbed is let go over about this many seconds.
    /// </summary>
    private const float ClockSettle = 0.25f;

    private static IEnumerable<NodeDef> Midi()
    {
        yield return new NodeDef(
            MidiTypeId, "MIDI In", ModuleCategories.Sources,
            [],
            [
                new PortSpec("pitch", PortKind.Scalar, 60f, 0f, 127f, -1, PortDisplay.Note) { Help = "The current note." },
                Num("gate", 0f, 0f, 1f) with
                {
                    Help = "High while a key is held. Drops for an instant as each new note lands, so "
                        + "legato notes still retrigger an envelope.",
                },
                Num("velocity", 0f, 0f, 1f) with { Help = "Follows note strength." },
                Num("trigger", 0f, 0f, 1f) with { Help = "Fires on each note start." },
            ],
            EmitMidi,
            "Keyboard or MIDI input. The index selects a polyphonic voice; 'channel' hears one of "
            + "an instrument's channels, or every one at 0.")
        {
            Extras = [new MidiExtra()],
        };

        yield return new NodeDef(
            ClockTypeId, "Clock In", ModuleCategories.Timing,
            [Domain("in", "The clock the beat is carried forward on between ticks: Time without a wire.")],
            [
                Num("beats") with
                {
                    Help = "The beats since it pressed Start, held while it is stopped. Patch it into "
                        + "the 'in' of a sequencer for one step per beat.",
                },
                Num("bpm", 120f, 20f, 300f) with { Help = "The tempo it is sending." },
                Num("running", 0f, 0f, 1f) with { Help = "High between Start and Stop." },
                Num("reset", 0f, 0f, 1f) with { Help = "Fires on Start." },
            ],
            EmitClock,
            "The clock of a drum machine or sequencer, followed.")
        {
            Extras = [new MidiClockExtra()],
        };
    }

    /// <summary>
    /// An instrument's clock, read as a line through its latest tick.
    /// </summary>
    /// <remarks>
    /// The shell publishes the beat as of the latest tick, which moves in steps
    /// of a twenty-fourth, and the rate it is moving at. Neither is enough on its
    /// own at 192,000 evaluations a second, so the program keeps the moment the
    /// beat last changed in a cell and runs the line <c>beat + rate × (in − at)</c>
    /// from there, against its own clock.
    /// <para>
    /// A tick lands early or late by the jitter of the cable and by wherever the
    /// speakers' buffer happened to be, so the line jumps a little each time it
    /// is re-anchored, and again when the tempo moves or a Stop takes the rate
    /// away. The jump is absorbed into a slip cell and let go over
    /// <see cref="ClockSettle"/>, which keeps 'beats' continuous; a jump of half
    /// a beat or more is the transport, not jitter, and is let straight through,
    /// as is a Start. Without a memory (ADR-0041) the beat is read as published.
    /// </para>
    /// </remarks>
    private static Slot[] EmitClock(Emitter em, EmitContext node)
    {
        var state = node.Extra<ExtraState>(MidiClockExtra.StateKey);
        var device = state?.Chosen(MidiClockExtra.DeviceField);

        if (string.IsNullOrWhiteSpace(device)) device = MidiSources.Keyboard;

        var beat = em.Live(MidiSignal.ClockKey(device, MidiSignal.Beat));
        var rate = em.Live(MidiSignal.ClockKey(device, MidiSignal.Rate));
        var bpm = em.Live(MidiSignal.ClockKey(device, MidiSignal.Bpm));
        var running = em.Live(MidiSignal.ClockKey(device, MidiSignal.Running));
        var starts = em.Live(MidiSignal.ClockKey(device, MidiSignal.Starts));

        var now = node[0];
        var memory = em.HasMemory();

        var beatCell = em.AllocateUnitSlot();
        var atCell = em.AllocateUnitSlot();
        var rateCell = em.AllocateUnitSlot();
        var slipCell = em.AllocateUnitSlot();

        var wasBeat = em.UnitRead(beatCell);
        var wasAt = em.UnitRead(atCell);
        var wasRate = em.UnitRead(rateCell);
        var wasSlip = em.UnitRead(slipCell);

        // Half a tick, because the beat moves by whole ones.
        var ticked = em.Binary(OpCode.Step, em.Constant(0.5f / MidiClock.TicksPerBeat), em.Unary(OpCode.Abs, em.Sub(beat, wasBeat)));

        // A Continue: the rate comes back before the next tick does, and the
        // line has to run from now rather than from the tick before the Stop.
        var wasStill = em.Sub(em.Constant(1f), em.Binary(OpCode.Step, em.Constant(1e-6f), em.Unary(OpCode.Abs, wasRate)));
        var woke = em.Mul(wasStill, em.Binary(OpCode.Step, em.Constant(1e-6f), em.Unary(OpCode.Abs, rate)));

        var arrived = em.Binary(OpCode.Max, ticked, woke);
        var at = em.Add(wasAt, em.Mul(arrived, em.Sub(now, wasAt)));
        var expected = em.Add(beat, em.Mul(memory, em.Mul(rate, em.Sub(now, at))));

        // Where the line as it was says the beat is now, against where the line
        // as it is now says: the difference is a tick landing off the line, a
        // tempo moving or a Stop, and none of them is time passing.
        var predicted = em.Add(wasBeat, em.Mul(wasRate, em.Sub(now, wasAt)));
        var carried = em.Mul(em.Sub(wasSlip, em.Sub(expected, predicted)), memory);

        var restarted = Pulse(em, starts);
        var moved = em.Binary(OpCode.Step, em.Constant(0.5f), em.Unary(OpCode.Abs, carried));
        var snap = em.Binary(OpCode.Max, moved, restarted);
        var settle = em.Binary(OpCode.Min, em.Constant(1f), em.Mul(em.Interval(), 1f / ClockSettle));
        var slip = em.Mul(carried, em.Sub(em.Constant(1f), em.Binary(OpCode.Max, snap, settle)));

        em.ClockWrite(beatCell, beat);
        em.ClockWrite(atCell, at);
        em.ClockWrite(rateCell, rate);
        em.ClockWrite(slipCell, slip);

        var beats = em.Add(expected, slip);

        return [beats, bpm, running, restarted];
    }

    /// <summary>
    /// A count that only goes up, differenced into an edge.
    /// </summary>
    /// <remarks>
    /// Nothing outside a program can hand it a pulse, since whoever fills the
    /// block in knows neither how long an evaluation is nor when one happens —
    /// the ear takes 192,000 a second and the eye sixty, off the same block. So
    /// what arrives is a count, and each path differences it against a cell to
    /// find its own edge at its own rate.
    /// <para>
    /// The count is kept in a clock cell rather than a signal one, which is clamped
    /// to the rails: the sixteenth note of a session would pin a tally, and the
    /// edge would stick high for good. Same reasoning as
    /// <see cref="OpCode.ClockWrite"/>.
    /// </para>
    /// <para>
    /// Multiplied by <see cref="Emitter.HasMemory"/> (ADR-0041), which is
    /// load-bearing: with no memory the cell reads nought at every pixel, so the
    /// count itself would look like a change.
    /// </para>
    /// </remarks>
    private static Slot Pulse(Emitter em, Slot count)
    {
        var cell = em.AllocateUnitSlot();
        var moved = em.Unary(OpCode.Abs, em.Sub(count, em.UnitRead(cell)));

        em.ClockWrite(cell, count);

        // Half a step, because the count moves by whole ones. Anything smaller
        // would be a threshold on a number that has no fractions in it.
        return em.Mul(em.Binary(OpCode.Step, em.Constant(0.5f), moved), em.HasMemory());
    }

    /// <summary>
    /// Four live inputs read straight out, and one of them differenced into an edge.
    /// </summary>
    /// <remarks>
    /// Nearly the whole module is <see cref="Emitter.Live"/>. What is not free is
    /// 'trigger', which is a count of notes struck turned into a pulse by
    /// <see cref="Pulse"/>. The gap it cuts in the gate is nought without a
    /// memory for the same reason the pulse is, or the gate would be held shut
    /// across the whole screen.
    /// </remarks>
    private static Slot[] EmitMidi(Emitter em, EmitContext node)
    {
        var state = node.Extra<ExtraState>(MidiExtra.StateKey);
        var device = state?.Chosen(MidiExtra.DeviceField);
        var index = (int)(state?.Number(MidiExtra.IndexField) ?? 0f);
        var channel = (int)(state?.Number(MidiExtra.ChannelField) ?? 0f);

        // A patch edited by hand is the one way an empty device arrives, and the
        // keyboard is the honest thing to fall back to: it is what a fresh module
        // listens to, and it is always there.
        if (string.IsNullOrWhiteSpace(device)) device = MidiSources.Keyboard;

        // The computer's keys have no channel, so a module asking for one there
        // hears the keys anyway rather than nothing.
        if (device != MidiSources.Keyboard) device = MidiSignal.Channeled(device, channel);

        Func<string, string> key = index == 0
            ? (signal => MidiSignal.AutoKey(device, node.Node, signal))
            : (signal => MidiSignal.Key(device, index, signal));
        var pitch = em.Live(key(MidiSignal.Pitch));
        var gate = em.Live(key(MidiSignal.Gate));
        var velocity = em.Live(key(MidiSignal.Velocity));
        var strikes = em.Live(key(MidiSignal.Strikes));

        var struck = Pulse(em, strikes);

        // The gate closed for the evaluation a new note lands on, which is what
        // makes a run of legato notes articulate: an ADSR reads the gap as the key
        // having been let go and taken again, and rises from wherever its level
        // had got to rather than from silence.
        var articulated = em.Mul(gate, em.Sub(em.Constant(1f), struck));

        return [pitch, articulated, velocity, struck];
    }
}

/// <summary>
/// Which instrument and polyphonic voice a MIDI In is listening to.
/// </summary>
/// <remarks>
/// The first extra in the engine that declares its editor rather than having one
/// written for it (ADR-0055): the other three needed a control of their own, and
/// this one needs a list of names. <see cref="Fields"/> is computed on every read
/// rather than held, because what it lists is what is plugged in right now — the
/// one thing the fixed kinds never had to do.
/// </remarks>
public sealed record MidiExtra : NodeExtra
{
    /// <summary>What this is filed under, in a saved patch and on a context.</summary>
    public const string StateKey = "midi";

    /// <summary>The fields selecting the instrument, its channel and the polyphonic voice.</summary>
    public const string DeviceField = "device";
    public const string IndexField = "index";
    public const string ChannelField = "channel";

    public override string Key => StateKey;

    public override IReadOnlyList<ExtraField> Fields =>
    [
        new ExtraField.Choice(
            DeviceField,
            "listens to",
            [.. MidiSources.All.Select(source => new ChoiceOption(source.Id, source.Name))],
            MidiSources.Keyboard),
        new ExtraField.Number(IndexField, "voice", new PortSpec("voice", PortKind.Scalar, 0f, 0f, 8f, -1, PortDisplay.Integer)),
        new ExtraField.Number(ChannelField, "channel", new PortSpec("channel", PortKind.Scalar, 0f, 0f, 16f, -1, PortDisplay.Integer)),
    ];

    /// <summary>
    /// The ordinary fold, and a word about a device that is not here, or a channel
    /// asked of the computer's keys, which have none. Reported
    /// rather than repaired, which is <see cref="SampleExtra"/>'s bargain with a
    /// missing file: a patch written on a machine with a keyboard still means that
    /// keyboard, and quietly moving it to the computer's keys would be a different
    /// patch wearing the same name.
    /// </summary>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env)
    {
        var chosen = Fields[0] is ExtraField.Choice field
            ? field.Value(node.StateOf(Key)?[DeviceField])
            : MidiSources.Keyboard;

        if (!MidiSources.All.Any(source => source.Id == chosen))
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' is listening to {chosen}, which is not here. It plays nothing "
                + "until that instrument is plugged in, or until another is picked in the panel.",
                IssueSeverity.Warning));
        }
        else if (chosen == MidiSources.Keyboard
            && Fields[2] is ExtraField.Number channel
            && channel.Value(node.StateOf(Key)?[ChannelField]) != 0f)
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' names a channel, and the computer keyboard has none. It hears the keys as before.",
                IssueSeverity.Warning));
        }

        return base.Fold(ctx, node, env);
    }

    /// <summary>
    /// What one may be set to, by id, as it stands right now — so an assistant
    /// asked to make a patch playable does not have to guess at a name for the
    /// keyboard.
    /// </summary>
    public override string Announce()
    {
        var offered = string.Join(", ", MidiSources.All.Select(source => source.Id));

        return $"  midi   device, which instrument it listens to — one of {offered}, "
            + "as a string; not a knob; voice, 0 for automatic assignment or 1 to 8; "
            + "channel, 0 for every channel or 1 to 16 for one of them";
    }
}

/// <summary>
/// Which instrument a Clock In follows. Filed under the same key as
/// <see cref="MidiExtra"/>, so a Clock In and a MIDI In name their instrument
/// the same way in a patch file and in the text.
/// </summary>
/// <remarks>
/// A fresh one follows the instrument known to conduct, or else the first one
/// plugged in, because the computer's keyboard has no clock and a module that
/// defaulted to it would be a module that did nothing until somebody found the
/// field.
/// </remarks>
public sealed record MidiClockExtra : NodeExtra
{
    public const string StateKey = MidiExtra.StateKey;

    public const string DeviceField = MidiExtra.DeviceField;

    public override string Key => StateKey;

    public override IReadOnlyList<ExtraField> Fields =>
    [
        new ExtraField.Choice(
            DeviceField,
            "follows",
            [.. MidiSources.All.Select(source => new ChoiceOption(source.Id, source.Name))],
            Conductor()),
    ];

    private static string Conductor()
    {
        var sources = MidiSources.All;

        return (sources.FirstOrDefault(source => source.Conducts) is { Id: not null } conductor
                ? conductor
                : sources.FirstOrDefault(source => source.Id != MidiSources.Keyboard)).Id
            ?? MidiSources.Keyboard;
    }

    /// <summary>
    /// The ordinary fold, and a word where the chosen instrument is gone or is the
    /// computer's keyboard, which keeps no clock. Reported rather than repaired,
    /// for the reason <see cref="MidiExtra"/> gives.
    /// </summary>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env)
    {
        var chosen = Fields[0] is ExtraField.Choice field
            ? field.Value(node.StateOf(Key)?[DeviceField])
            : MidiSources.Keyboard;

        if (chosen == MidiSources.Keyboard)
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' is following the computer keyboard, which keeps no clock. "
                + "Its beats stay at nought until an instrument is picked in the panel.",
                IssueSeverity.Warning));
        }
        else if (!MidiSources.All.Any(source => source.Id == chosen))
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' is following {chosen}, which is not here. Its beats stay where they were "
                + "until that instrument is plugged in, or until another is picked in the panel.",
                IssueSeverity.Warning));
        }

        return base.Fold(ctx, node, env);
    }

    public override string Announce()
    {
        var offered = string.Join(", ", MidiSources.All.Where(source => source.Id != MidiSources.Keyboard).Select(source => source.Id));

        return $"  midi   device, which instrument's clock it follows — one of {offered}, as a string; not a knob";
    }
}
