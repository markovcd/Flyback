using System.Text.Json.Serialization;

namespace Flyback.Core.Graph;

/// <summary>Which knob a socket follows, and over what range.</summary>
/// <param name="Control">The <see cref="PatchControl.Id"/> it follows.</param>
/// <param name="Min">What the socket reads with the knob all the way down.</param>
/// <param name="Max">What it reads all the way up; below <paramref name="Min"/> turns the knob round.</param>
public readonly record struct ControlLink(Guid Control, float Min, float Max)
{
    /// <summary>
    /// The socket's <see cref="PortSpec.Knee"/> when it was linked, so the knob
    /// sweeps the range as the socket's own slider does.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float Knee { get; init; }

    /// <summary>
    /// A link over <paramref name="socket"/>'s own range, widened to take in
    /// <paramref name="resting"/>, and swept as its slider is.
    /// </summary>
    public static ControlLink For(Guid control, PortSpec socket, float resting) =>
        new(control, Math.Min(socket.Min, resting), Math.Max(socket.Max, resting)) { Knee = socket.Knee };

    /// <summary>
    /// This link swept in decades, or evenly where <paramref name="inDecades"/> is
    /// false. In decades it takes <paramref name="socket"/>'s knee, or where the
    /// socket has none, one fitted to the range: its bottom where that is above
    /// nought, so the whole sweep is decades, and otherwise three decades of it.
    /// </summary>
    public ControlLink Swept(bool inDecades, PortSpec socket)
    {
        if (!inDecades) return this with { Knee = 0f };
        if (socket.Knee > 0f) return this with { Knee = socket.Knee };

        var low = Math.Min(Min, Max);
        var span = Math.Abs(Max - Min);

        return this with { Knee = low > 0f ? low : span / 1000f };
    }

    /// <summary>What the socket reads with the knob at <paramref name="value"/>.</summary>
    public float At(float value) => Taper.At(value, Min, Max, Knee);

    /// <summary>Where the knob has to sit for the socket to read <paramref name="reading"/>, held to 0..1.</summary>
    public float Inverse(float reading) =>
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        Max == Min ? 0f : (float)Taper.Travel(reading, Min, Max, Knee);
}