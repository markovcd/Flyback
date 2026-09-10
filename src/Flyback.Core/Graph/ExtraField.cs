using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flyback.Core.Graph;

/// <summary>
/// One editable value of a plugin's extra, described rather than drawn.
/// </summary>
/// <remarks>
/// The whole of the declarative route (ADR-0055): a plugin says what it carries
/// and the App draws it, so no plugin ships a control and no plugin binary is
/// pinned to the Avalonia a given build shipped.
/// <para>
/// The vocabulary is deliberately short — every shape here is public API that
/// cannot be taken back — and what it cannot express is a control of its own: a
/// keyboard, a waveform, a list you reorder. The engine's own three kinds each
/// needed one, and none of them goes through here.
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
    /// The stored value held to what this field can mean, and the field's own
    /// default where it means nothing at all.
    /// </summary>
    /// <remarks>
    /// Every read goes through here rather than trusting the file: a patch is text
    /// somebody may have edited. It is also what a fresh instance is seeded with,
    /// since "no value yet" is the same question as "a value that means nothing".
    /// </remarks>
    public abstract JsonNode Sane(JsonNode? stored);

    /// <summary>This field's value, written the way the inspector shows it.</summary>
    public abstract string Format(JsonNode? stored);

    /// <summary>
    /// A number, with everything a knob has: a range, a display and whether it
    /// rests between whole numbers.
    /// </summary>
    /// <remarks>
    /// A <see cref="PortSpec"/> rather than a range of its own, so the App draws
    /// it and <see cref="PortDisplay"/> writes 57 as "A3" — a plugin's field reads
    /// the same as a knob two rows above because it is the same code.
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
    /// One of a list of named things — an instrument to listen to, a port to open,
    /// a mode to run in.
    /// </summary>
    /// <remarks>
    /// What makes it different from a number or a switch is that the options are
    /// not fixed: a list of instruments is a fact about the room and changes while
    /// the program runs, so <see cref="Options"/> is read each time the panel is
    /// drawn — see <see cref="NodeExtra.Fields"/>, which a kind may compute rather
    /// than hold.
    /// </remarks>
    /// <param name="Options">
    /// What there is to choose from right now. May be empty, which is a real
    /// answer: nothing is plugged in.
    /// </param>
    /// <param name="Fallback">
    /// What a fresh instance carries, and what a stored value that is not a string
    /// falls back to. Not required to be in <see cref="Options"/>.
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
        /// Deliberately not held to <see cref="Options"/>: an id that is not in the
        /// list is a device switched off rather than a broken value, and falling
        /// back to the default would quietly rewrite the patch the first time it
        /// was opened on a machine where the thing was unplugged. See
        /// <see cref="SampleExtra"/>, which reports rather than forgets.
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
/// name a person reads. Kept apart so a saved patch goes on meaning the same
/// thing when a device is renamed or moved to another port.
/// </summary>
/// <param name="Id">Stable, and what ends up in the file.</param>
/// <param name="Name">What the picker shows.</param>
public readonly record struct ChoiceOption(string Id, string Name);
