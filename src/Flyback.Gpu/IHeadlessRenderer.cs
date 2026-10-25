using Flyback.Engine.Compile;
using Flyback.Engine.Render;

namespace Flyback.Gpu;

/// <summary>A patch's frames drawn on the GPU with no window, read back into memory.</summary>
internal interface IHeadlessRenderer : IFrameRenderer, IDisposable
{
    /// <summary>What drew it, for saying so: the API and the device.</summary>
    string Description { get; }

    /// <summary>Builds the patch's shader, so a patch the GPU cannot draw is known before any frame. Null on success.</summary>
    string? Prepare(CompiledPatch patch);

    /// <summary>Clears the feedback history, so the next frame starts from black.</summary>
    void Reset();
}
