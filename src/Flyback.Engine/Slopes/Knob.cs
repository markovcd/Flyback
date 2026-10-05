namespace Flyback.Engine.Slopes;

/// <summary>A knob a slope can be taken against: a socket's own, or a panel knob.</summary>
/// <param name="Key">The live input it is read from.</param>
/// <param name="Owner">The module, or the panel knob's id.</param>
/// <param name="Port">The socket, or -1 for a panel knob.</param>
/// <param name="Label">The module's title and the socket's name, or the panel knob's name.</param>
/// <param name="Value">Where it rests.</param>
/// <param name="Min">The low end of its travel.</param>
/// <param name="Max">The high end of its travel.</param>
internal sealed record Knob(string Key, Guid Owner, int Port, string Label, float Value, float Min, float Max)
{
    /// <summary>The whole travel, which a slope is scaled by to compare knobs in different units.</summary>
    public float Range => Max > Min ? Max - Min : 1f;
}
