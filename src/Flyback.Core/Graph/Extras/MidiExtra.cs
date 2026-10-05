using Flyback.Core.Compile;

namespace Flyback.Core.Graph.Extras;

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
    /// <summary>The field choosing the polyphonic voice.</summary>
    public const string IndexField = "index";
    /// <summary>The field choosing the MIDI channel.</summary>
    public const string ChannelField = "channel";
    /// <summary>The field choosing how many voices it plays down one polyphonic wire.</summary>
    internal const string VoicesField = "voices";

    /// <inheritdoc/>
    public override string Key => StateKey;

    /// <inheritdoc/>
    public override IReadOnlyList<ExtraField> Fields =>
    [
        new ExtraField.Choice(
            DeviceField,
            "listens to",
            [.. MidiSources.All.Select(source => new ChoiceOption(source.Id, source.Name))],
            MidiSources.Keyboard) { Help = "Where the notes come from: the computer keyboard or an instrument." },
        new ExtraField.Number(IndexField, "voice", new PortSpec("voice", PortKind.Scalar, 0f, 0f, 8f, -1, PortDisplay.Integer))
        {
            Help = "Which of the notes held at once it plays: 0 shares them out, 1 to 8 takes that one.",
        },
        new ExtraField.Number(ChannelField, "channel", new PortSpec("channel", PortKind.Scalar, 0f, 0f, 16f, -1, PortDisplay.Integer))
        {
            Help = "0 hears every channel, 1 to 16 only that one.",
        },
        new ExtraField.Number(VoicesField, "voices", new PortSpec("voices", PortKind.Scalar, 1f, 1f, VoiceCounts.Most, -1, PortDisplay.Integer))
        {
            Help = "How many notes it plays at once down its one set of wires, each a voice of a polyphonic "
                + "wire: from 'voice' up, or from 1 when 'voice' shares them out.",
        },
    ];

    /// <summary>
    /// How many voices a placed MIDI In plays, never past the last of the
    /// <see cref="VoiceCounts.Most"/> its first one leaves.
    /// </summary>
    internal int Voices(NodeInstance node)
    {
        var held = node.StateOf(Key);
        var voices = Fields[3] is ExtraField.Number count ? (int)count.Value(held?[VoicesField]) : 1;
        var first = Math.Max(1, Fields[1] is ExtraField.Number index ? (int)index.Value(held?[IndexField]) : 0);

        return Math.Clamp(voices, 1, VoiceCounts.Most - first + 1);
    }

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
               + "channel, 0 for every channel or 1 to 16 for one of them; voices, 1 to 8 notes "
               + "at once down one polyphonic wire, from 'voice' up";
    }
}