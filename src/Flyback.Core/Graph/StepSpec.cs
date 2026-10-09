namespace Flyback.Core.Graph;

/// <summary>
/// The notes a module carries, and how one of them is read and edited. One
/// record rather than three members on the definition, because the display and
/// range only make sense with a sequence of notes.
/// </summary>
/// <param name="Default">The tune a freshly placed instance carries.</param>
/// <param name="Display">How a step's value reads: by name on the Note Sequencer.</param>
/// <param name="Range">The span a step's value is edited within.</param>
public sealed record StepSpec(
    IReadOnlyList<Step> Default,
    PortDisplay Display,
    (float Min, float Max) Range)
{
    /// <summary>
    /// A step's value described as though it were a socket, so the editor formats
    /// and snaps it with the same code every knob uses.
    /// </summary>
    public PortSpec AsPort => new(
        "value", PortKind.Scalar, 0f, Range.Min, Range.Max, -1, Display);
}
