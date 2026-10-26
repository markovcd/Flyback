using Flyback.Engine.Compile;
using Flyback.Engine.Render;

namespace Flyback.Gpu;

/// <summary>
/// Draws a patch's frames through Metal with no window, read back into memory: what
/// <see cref="HeadlessRenderer"/> does on OpenGL, on a Mac or an iPhone.
/// </summary>
/// <remarks>Called from one thread at a time, any thread.</remarks>
internal sealed class MetalHeadlessRenderer : IHeadlessRenderer
{
    private readonly MetalDevice device;
    private readonly MetalFrameRenderer renderer;

    private MetalHeadlessRenderer(MetalDevice device, bool overlapped)
    {
        this.device = device;
        renderer = new MetalFrameRenderer(device, overlapped);
    }

    /// <summary>What drew it, for saying so.</summary>
    public string Description => $"Metal on {device.Name}";

    /// <summary>A renderer on this machine's Metal device, or null with <paramref name="why"/> saying what stood in the way.</summary>
    /// <param name="overlapped">Whether each frame hands back the one before it, for a preview (see <see cref="MetalFrameRenderer"/>).</param>
    public static MetalHeadlessRenderer? Open(out string? why, bool overlapped = false)
    {
        var device = MetalDevice.Open(out why);

        return device is null ? null : new MetalHeadlessRenderer(device, overlapped);
    }

    /// <summary>Builds the patch's library, so a patch Metal cannot draw is known before any frame. Null on success.</summary>
    public string? Prepare(CompiledPatch patch) => renderer.SetPatch(patch);

    /// <summary>Clears the feedback history, so the next frame starts from black.</summary>
    public void Reset() => renderer.Rewind();

    public Smoothing Smoothing
    {
        get => renderer.Smoothing;
        set => renderer.Smoothing = value;
    }

    public void Render(
        CompiledPatch patch,
        double time,
        int width,
        int height,
        Span<byte> destination,
        int stride,
        LiveValues? live = null)
    {
        if (width <= 0 || height <= 0) return;
        if (destination.Length < (long)stride * height)
            throw new ArgumentException("Destination is smaller than stride * height.", nameof(destination));

        // Once per frame, as the preview does: the constants, the pictures and a
        // Scope's trace all change without the library's text changing.
        var failure = renderer.SetPatch(patch)
            ?? renderer.Frame(new SurfaceSize(width, height), time, live, destination, stride);

        if (failure is not null) throw new InvalidOperationException(failure);
    }

    public void Dispose()
    {
        renderer.Dispose();
        device.Dispose();
    }
}
