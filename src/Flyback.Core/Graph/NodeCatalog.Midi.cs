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

    private static IEnumerable<NodeDef> Midi()
    {
        yield return new NodeDef(
            MidiTypeId, "MIDI In", "Source",
            [],
            [
                new PortSpec("pitch", PortKind.Scalar, 60f, 0f, 127f, -1, PortDisplay.Note),
                Num("gate", 0f, 0f, 1f),
                Num("velocity", 0f, 0f, 1f),
                Num("trigger", 0f, 0f, 1f),
            ],
            EmitMidi,
            "Keyboard or MIDI input. 'pitch' is the current note; 'gate' is high while a key is held; "
            + "'velocity' follows note strength; 'trigger' fires on each note start. The index selects a polyphonic voice." )
        {
            Extras = [new MidiExtra()],
        };
    }

    /// <summary>
    /// Four live inputs read straight out, and one of them differenced into an
    /// edge.
    /// </summary>
    /// <remarks>
    /// Nearly the whole module is <see cref="Emitter.Live"/>: the instrument is
    /// filled in from outside while the program runs, and reading it is one op
    /// per signal. What is not free is 'trigger', and the reason is the same wall
    /// the Sample's own trigger met from the other side.
    /// <para>
    /// Nothing outside a program can hand it a pulse. A pulse means "high for
    /// exactly one evaluation", and whoever is filling the block in knows neither
    /// how long an evaluation is nor when one happens — the ear takes 192,000 a
    /// second and the eye sixty, off the same block. So what arrives is a count of
    /// notes struck, which only ever goes up, and the pulse is made here by
    /// differencing it against a cell: each path finds its own edge, at its own
    /// rate, on the evaluation the count moved.
    /// </para>
    /// <para>
    /// The count is kept in a clock cell rather than a signal one. A signal cell
    /// is clamped to the rails, which is right for a value a wire can reach and
    /// wrong for a tally: the sixteenth note of a session would pin it, every
    /// evaluation after would look like a fresh strike, and the trigger would
    /// stick high for good. The same reasoning as
    /// <see cref="OpCode.ClockWrite"/>, which the Sample reached for to hold a
    /// playhead.
    /// </para>
    /// <para>
    /// Both the edge and the gap it cuts in the gate are multiplied by
    /// <see cref="Emitter.HasMemory"/>, so the picture is given a chosen answer
    /// rather than an emergent one — ADR-0041's rule. It is load-bearing here
    /// rather than tidy: with no memory the cell reads nought at every pixel, so
    /// the count itself would look like a change, and one note into a session the
    /// trigger would be stuck high and the gate held shut across the whole screen.
    /// </para>
    /// </remarks>
    private static Slot[] EmitMidi(Emitter em, EmitContext node)
    {
        var state = node.Extra<ExtraState>(MidiExtra.StateKey);
        var device = state?.Chosen(MidiExtra.DeviceField);
        var index = (int)(state?.Number(MidiExtra.IndexField) ?? 1f);

        // A patch edited by hand is the one way an empty device arrives, and the
        // keyboard is the honest thing to fall back to: it is what a fresh module
        // listens to, and it is always there.
        if (string.IsNullOrWhiteSpace(device)) device = MidiSources.Keyboard;

        var pitch = em.Live(MidiSignal.Key(device, index, MidiSignal.Pitch));
        var gate = em.Live(MidiSignal.Key(device, index, MidiSignal.Gate));
        var velocity = em.Live(MidiSignal.Key(device, index, MidiSignal.Velocity));
        var strikes = em.Live(MidiSignal.Key(device, index, MidiSignal.Strikes));

        var cell = em.AllocateUnitSlot();
        var moved = em.Unary(OpCode.Abs, em.Sub(strikes, em.UnitRead(cell)));

        em.ClockWrite(cell, strikes);

        // Half a note, because the count moves by whole ones. Anything smaller
        // would be a threshold on a number that has no fractions in it.
        var struck = em.Mul(em.Binary(OpCode.Step, em.Constant(0.5f), moved), em.HasMemory());

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
/// written for it ([0055](0055-a-plugins-extra-declares-its-editor.md)). The
/// other three needed a control of their own — a step list, twelve toggles, a
/// file dialog — and this one needs a list of names, which is exactly what the
/// declarative route already draws.
/// <para>
/// <see cref="Fields"/> is computed on every read rather than held, because what
/// it lists is what is plugged in right now. That is the one thing the two fixed
/// kinds never had to do: a number's range does not change while the panel is
/// open, and a device list does.
/// </para>
/// </remarks>
public sealed record MidiExtra : NodeExtra
{
    /// <summary>What this is filed under, in a saved patch and on a context.</summary>
    public const string StateKey = "midi";

    /// <summary>The fields selecting the instrument and polyphonic voice.</summary>
    public const string DeviceField = "device";
    public const string IndexField = "index";

    public override string Key => StateKey;

    public override IReadOnlyList<ExtraField> Fields =>
    [
        new ExtraField.Choice(
            DeviceField,
            "listens to",
            [.. MidiSources.All.Select(source => new ChoiceOption(source.Id, source.Name))],
            MidiSources.Keyboard),
        new ExtraField.Number(IndexField, "voice", new PortSpec("voice", PortKind.Scalar, 1f, 1f, 8f, 1, PortDisplay.Integer)),
    ];

    /// <summary>
    /// The ordinary fold, and a word about a device that is not here.
    /// </summary>
    /// <remarks>
    /// Reported rather than repaired, which is <see cref="SampleExtra"/>'s bargain
    /// with a missing file. A patch written on a machine with a keyboard on it
    /// still means that keyboard when it is opened on a machine without one, and
    /// quietly moving it to the computer's keys would be a different patch wearing
    /// the same name. So the choice stands and the silence is explained.
    /// </remarks>
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
            + "as a string; not a knob; voice, the polyphonic index from 1 to 8";
    }
}
