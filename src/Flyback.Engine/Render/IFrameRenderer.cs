using Flyback.Engine.Compile;

namespace Flyback.Engine.Render;

/// <summary>
/// Draws a patch's frames in order, keeping the history a feedback loop reads
/// between them: <see cref="SynthRenderer"/> on the processor, or the GPU.
/// </summary>
internal interface IFrameRenderer
{
    /// <summary>Renders one frame into a BGRA8888 buffer, top row first.</summary>
    void Render(
        CompiledPatch patch,
        double time,
        int width,
        int height,
        Span<byte> destination,
        int stride,
        LiveValues? live = null);
}
