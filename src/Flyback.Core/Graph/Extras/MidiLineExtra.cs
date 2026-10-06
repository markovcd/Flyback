namespace Flyback.Core.Graph.Extras;

/// <summary>Which voice and channel of a MIDI file a MIDI File plays.</summary>
/// <remarks>
/// The other half of <see cref="MidiFileExtra"/>, which folds the two together: the
/// file is a path and these are numbers, and a declared field cannot hold the path
/// without the editor writing over it.
/// </remarks>
public sealed record MidiLineExtra : NodeExtra
{
    /// <summary>What this is filed under, in a saved patch.</summary>
    public const string Name = "midi";

    /// <summary>The field choosing the voice.</summary>
    public const string VoiceField = "voice";

    /// <summary>The field choosing the MIDI channel.</summary>
    public const string ChannelField = "channel";

    /// <inheritdoc/>
    public override string Key => Name;

    /// <inheritdoc/>
    public override IReadOnlyList<ExtraField> Fields =>
    [
        new ExtraField.Number(VoiceField, "voice", new PortSpec("voice", PortKind.Scalar, 0f, 0f, 8f, -1, PortDisplay.Integer))
        {
            Help = "0 plays one note at a time, the newest held. 1 to 8 plays that one of the notes held at once.",
        },
        new ExtraField.Number(ChannelField, "channel", new PortSpec("channel", PortKind.Scalar, 0f, 0f, 16f, -1, PortDisplay.Integer))
        {
            Help = "0 hears every channel, 1 to 16 only that one.",
        },
    ];

    /// <summary>The voice this instance plays.</summary>
    public static int Voice(NodeInstance node) => (int)Of(node, VoiceField);

    /// <summary>The channel this instance hears.</summary>
    public static int Channel(NodeInstance node) => (int)Of(node, ChannelField);

    private static float Of(NodeInstance node, string field) =>
        new MidiLineExtra().Fields.OfType<ExtraField.Number>().First(f => f.Key == field).Value(node.StateOf(Name)?[field]);

    /// <inheritdoc/>
    public override string Announce() =>
        "  midi   voice, 0 for one note at a time or 1 to 8 for one of the notes held at once; "
        + "channel, 0 for every channel or 1 to 16 for one of them";
}
