namespace Flyback.Core.Graph;

/// <summary>
/// One part's level in one section of an Arrangement.
/// </summary>
/// <param name="Value">An ordinary signal: a level, or anything else a part should hold for a section.</param>
/// <param name="Glides">
/// Whether it travels there from the section before's level across the whole section,
/// rather than arriving at the start of it.
/// </param>
public readonly record struct PartLevel(float Value, bool Glides = false)
{
    /// <summary>The same level with a value the emit can use.</summary>
    internal PartLevel Sane() => this with { Value = float.IsFinite(Value) ? Value : 0f };
}
