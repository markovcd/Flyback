namespace Flyback.Core.Compile;

/// <summary>
/// How often an op has to be run to draw a frame, which is decided by the
/// furthest of the three loads anything upstream of it reaches.
/// </summary>
public enum EvaluationStage
{
    /// <summary>
    /// Settled once for the whole picture: literals, the clock, the frame's
    /// shape and whatever is being played into it. Everything a
    /// <see cref="OpCode.Const"/>, <see cref="OpCode.LoadT"/>,
    /// <see cref="OpCode.LoadAspect"/> or <see cref="OpCode.LoadLive"/> feeds
    /// and that no coordinate reaches.
    /// </summary>
    Frame,

    /// <summary>Settled once per scanline: whatever <see cref="OpCode.LoadY"/> reaches and <see cref="OpCode.LoadX"/> does not.</summary>
    Row,

    /// <summary>What is left, and the only part a pixel actually has to pay for.</summary>
    Pixel,
}