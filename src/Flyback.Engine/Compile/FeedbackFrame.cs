using System.Diagnostics.CodeAnalysis;
using Flyback.Core.Compile;

namespace Flyback.Engine.Compile;

/// <summary>
/// The previous frame, exposed to <see cref="OpCode.SampleFeedback"/>. Stored
/// as linear float RGB so repeated feedback passes don't quantize to 8 bits.
/// </summary>
[SuppressMessage("Design", "CA1051", Justification = "Read per pixel by feedback.")]
public readonly struct FeedbackFrame(float[]? pixels, int width, int height, double age = 0d)
{
    public readonly float[]? Pixels = pixels;
    public readonly int Width = width;
    public readonly int Height = height;

    /// <summary>Seconds since this frame was drawn, for <see cref="OpCode.LoadFeedbackAge"/>.</summary>
    public readonly double Age = age;

    public float Aspect => Height == 0 ? 1f : (float)Width / Height;
}
