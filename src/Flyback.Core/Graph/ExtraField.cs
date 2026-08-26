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
/// The vocabulary is deliberately two words long. Every shape here is public API
/// that cannot be taken back, and the pressure to guess at a third — a choice, a
/// path, a list of records — should be answered by a plugin that is actually
/// blocked rather than by imagining one. These two are the ones with a renderer
/// that already exists and is already tested.
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
