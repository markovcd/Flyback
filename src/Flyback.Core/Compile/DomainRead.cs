namespace Flyback.Core.Compile;

/// <summary>Which of x, y and t a piece of lowering read, and the registers they were.</summary>
/// <remarks>
/// Lowering it again reads the same wherever those registers are the same, which
/// is what lets a module a sweep reads be lowered once rather than once a place.
/// </remarks>
internal readonly record struct DomainRead(int Mask, Slot X, Slot Y, Slot T)
{
    public const int ReadsX = 1, ReadsY = 2, ReadsT = 4;
}
