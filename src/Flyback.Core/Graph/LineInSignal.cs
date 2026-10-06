namespace Flyback.Core.Graph;

/// <summary>
/// What a Line In reads, and how a program names it.
/// </summary>
/// <remarks>
/// The sound's renderer writes these two live inputs once a frame from whatever the
/// capture device heard (ADR-0178), so the program reads them like any played value
/// (see <see cref="Compile.OpCode.LoadLive"/>) and every backend agrees on them.
/// </remarks>
public static class LineInSignal
{
    /// <summary>The left channel of the sound input, as it was heard a moment ago.</summary>
    public const string Left = "line-in/left";

    /// <summary>The right channel, which is the left again for a mono microphone.</summary>
    public const string Right = "line-in/right";
}
