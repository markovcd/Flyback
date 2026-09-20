namespace Flyback.App.Capture;

/// <summary>
/// Somebody who wants the frames the GPU just drew. The renderer knows this and
/// nothing else about recording — it does not know what a file is, only that
/// bytes are wanted and how much it costs to fetch them.
/// </summary>
/// <remarks>
/// <see cref="Accept"/> is called on the render thread with the context current,
/// so an implementation may copy and must not encode, write or wait. The span is
/// only valid for the call.
/// </remarks>
internal interface IFrameSink
{
    /// <summary>
    /// One frame, tightly packed RGBA bottom-up — which is how OpenGL hands
    /// pixels back, and is the caller's problem to turn into a picture.
    /// </summary>
    void Accept(ReadOnlySpan<byte> rgba, int width, int height);
}
