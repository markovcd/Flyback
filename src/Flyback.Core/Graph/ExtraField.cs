using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flyback.Core.Graph;

/// <summary>
/// One editable value of a plugin's extra, described rather than drawn.
/// </summary>
/// <remarks>
/// The whole of the declarative route
/// ([0055](0055-a-plugins-extra-declares-its-editor.md)): a plugin says what it
/// carries, and the App draws it. No plugin ships a control, so Avalonia never
/// becomes an assembly the host has to own, and a plugin binary is not pinned to
/// the version of it a given build shipped.
/// <para>
/// The vocabulary is deliberately short. Every shape here is public API that
/// cannot be taken back, so a fourth word waits for a module that is actually
/// blocked rather than for an imagined one. The first two shipped together; the
/// third — <see cref="Choice"/> — arrived when the MIDI Input needed to say
/// *which* keyboard, which is neither a number nor a switch. A path and a list of
/// records are still imagined, and are still not here.
/// </para>
/// <para>
/// What this cannot express is a control of its own: a keyboard, a waveform, a
/// list you reorder. That is the price of the route, and the engine's own three
/// kinds are the proof it is a real one — all three needed a bespoke control and
/// none of them goes through here.
/// </para>
/// </remarks>
/// <param name="Key">
/// What this value is filed under, inside the object the extra owns. Stable: it
/// is in every saved patch that holds the module.
/// </param>
/// <param name="Label">What the inspector writes beside it.</param>
public abstract record ExtraField(string Key, string Label)
{
    /// <summary>
    /// The stored value held to what this field can actually mean, and the
    /// field's own default where it means nothing at all.
    /// </summary>
    /// <remarks>
    /// Every read goes through here rather than trusting the file, for the reason
    /// <see cref="Step.Sane"/> exists: a patch is text somebody may have edited,
    /// so the shape it can hold is wider than the shape that means anything. It
    /// is also what a fresh instance is seeded with, since "no value yet" is the
    /// same question as "a value that means nothing".
    /// </remarks>
    public abstract JsonNode Sane(JsonNode? stored);

    /// <summary>This field's value, written the way the inspector shows it.</summary>
    public abstract string Format(JsonNode? stored);

    /// <summary>
    /// A number, with everything a knob has: a range, a display and whether it
    /// rests between whole numbers.
    /// </summary>
    /// <remarks>
    /// A <see cref="PortSpec"/> rather than a range of its own, and that is the
    /// reuse this shape is built on: the App already draws one of those, and
    /// <see cref="PortDisplay"/> already writes 57 as "A3" and -3 as "1 ms". A
    /// plugin's field gets all of it for nothing, and reads the same as a knob
    /// two rows above it because it is drawn by the same code.
    /// </remarks>
    public sealed record Number(string Key, string Label, PortSpec Spec) : ExtraField(Key, Label)
    {
        public override JsonNode Sane(JsonNode? stored) => JsonValue.Create(Value(stored));

        public override string Format(JsonNode? stored) => Spec.Format(Value(stored));

        /// <summary>This field's value as the number it is, always inside the range.</summary>
        public float Value(JsonNode? stored)
        {
            var value = stored?.GetValueKind() == JsonValueKind.Number
                && stored.AsValue().TryGetValue<float>(out var stated)
                    ? stated
                    : Spec.Default;

            return float.IsFinite(value) ? Math.Clamp(value, Spec.Min, Spec.Max) : Spec.Default;
        }
    }

    /// <summary>
    /// One of a list of named things — an instrument to listen to, a port to
    /// open, a mode to run in.
    /// </summary>
    /// <remarks>
    /// The third word of the vocabulary, and the one the two above said should
    /// wait for a plugin that was actually blocked rather than an imagined one.
    /// The MIDI Input is that module: what it carries is *which* keyboard, which
    /// is neither a number nor a switch, and no arrangement of the other two says
    /// it.
    /// <para>
    /// What makes it different from them is that the options are not fixed. A
    /// number's range is a fact about the field; a list of instruments is a fact
    /// about the room, and it changes while the program is running. So
    /// <see cref="Options"/> is read each time the panel is drawn — see
    /// <see cref="NodeExtra.Fields"/>, which a kind may compute rather than hold.
    /// </para>
    /// </remarks>
    /// <param name="Options">
    /// What there is to choose from, as it stands right now. May be empty, which
    /// is a real answer: nothing is plugged in.
    /// </param>
    /// <param name="Fallback">
    /// What a fresh instance carries, and what a stored value that is not a
    /// string falls back to. Not required to be in <see cref="Options"/> — the
    /// one the field means may be unplugged at the moment it is asked.
    /// </param>
    public sealed record Choice(
        string Key,
        string Label,
        IReadOnlyList<ChoiceOption> Options,
        string Fallback = "") : ExtraField(Key, Label)
    {
        public override JsonNode Sane(JsonNode? stored) => JsonValue.Create(Value(stored));

        public override string Format(JsonNode? stored) => Name(Value(stored));

        /// <summary>
        /// What is chosen, which is whatever was stored.
        /// </summary>
        /// <remarks>
        /// Deliberately not held to <see cref="Options"/>, and this is the one
        /// place a field's tidying stops short of what it can mean. An id that is
        /// not in the list is not a broken value: it is a device that is switched
        /// off, and a patch saved with it should still name it when it comes
        /// back. Falling back to the default would quietly rewrite the patch to
        /// mean something else the first time it was opened on a machine where
        /// the thing was unplugged — which is the same mistake a Sample would
        /// make if a missing file cleared the path. See <see cref="SampleExtra"/>,
        /// which reports rather than forgets.
        /// </remarks>
        public string Value(JsonNode? stored) =>
            stored?.GetValueKind() == JsonValueKind.String
            && stored.AsValue().TryGetValue<string>(out var chosen)
            && !string.IsNullOrWhiteSpace(chosen)
                ? chosen
                : Fallback;

        /// <summary>
        /// What to call <paramref name="id"/>, and the id itself where nothing in
        /// the list answers to it — so a device that has gone reads as its own
        /// name rather than as a blank.
        /// </summary>
        public string Name(string id)
        {
            foreach (var option in Options)
                if (option.Id == id)
                    return option.Name;

            return string.IsNullOrEmpty(id) ? "nothing" : $"{id} (not here)";
        }
    }

    /// <summary>Something that is either on or off.</summary>
    /// <param name="On">What a fresh instance carries.</param>
    public sealed record Toggle(string Key, string Label, bool On = false) : ExtraField(Key, Label)
    {
        public override JsonNode Sane(JsonNode? stored) => JsonValue.Create(Value(stored));

        public override string Format(JsonNode? stored) => Value(stored) ? "on" : "off";

        /// <summary>This field's value as the switch it is.</summary>
        public bool Value(JsonNode? stored) => stored?.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => On,
        };
    }
}

/// <summary>
/// One entry of a <see cref="ExtraField.Choice"/>: the id a patch stores, and the
/// name a person reads.
/// </summary>
/// <remarks>
/// The two are kept apart on purpose. A saved patch must go on meaning the same
/// thing when a device is renamed, moved to another port or read on another
/// machine, so the id is what is written down and the name is only ever shown.
/// </remarks>
/// <param name="Id">Stable, and what ends up in the file.</param>
/// <param name="Name">What the picker shows.</param>
public readonly record struct ChoiceOption(string Id, string Name);
