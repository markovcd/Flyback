using System.Text.Json.Serialization;

namespace Flyback.Core.Graph;

/// <summary>
/// A knob on the patch's control panel, which any number of sockets can follow
/// through <see cref="ControlMap"/>.
/// </summary>
/// <remarks>
/// A hardware controller is bound to a knob, never to a socket, so learning a
/// controller and linking a knob are the same thing. A linked socket reads the knob
/// as a live value under <see cref="KeyOf"/>, so turning it recompiles nothing
/// (ADR-0086).
/// </remarks>
public sealed class PatchControl
{
    /// <inheritdoc cref="NodeInstance.NameLimit"/>
    public const int NameLimit = NodeInstance.NameLimit;

    /// <summary>Names this knob within its patch.</summary>
    public required Guid Id { get; init; }

    /// <summary>What the panel calls it.</summary>
    public string Name
    {
        get;
        set => field = Named(value);
    } = "Knob";

    /// <summary>Where it rests, 0 to 1: what a saved patch opens at and a fresh live block is seeded with.</summary>
    public float Value
    {
        get;
        set => field = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
    }

    /// <summary>The hardware controller it follows, or null for a knob only turned on screen.</summary>
    public MidiBinding? Midi { get; set; }

    /// <summary>
    /// What the text calls it, where that is not its name: <c>cutoff</c> for a
    /// knob labeled "Filter cutoff". Null for one the text calls by its name, and
    /// for one made on the canvas.
    /// </summary>
    public string? Word { get; set; }

    /// <summary>Whether randomizing the panel leaves it where it is.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Held { get; set; }

    /// <summary>What a program reading this knob calls it in <c>CompiledPatch.LiveInputs</c>.</summary>
    public static string KeyOf(Guid control) => $"control/{control:N}";

    /// <inheritdoc cref="KeyOf(Guid)"/>
    public string Key => KeyOf(Id);

    /// <summary>A copy of this knob, with the same id.</summary>
    public PatchControl Clone() => new() { Id = Id, Name = Name, Value = Value, Midi = Midi, Word = Word, Held = Held };

    private static string Named(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length > NameLimit) trimmed = TextLimit.Clip(trimmed, NameLimit).TrimEnd();

        return trimmed.Length == 0 ? "Knob" : trimmed;
    }
}