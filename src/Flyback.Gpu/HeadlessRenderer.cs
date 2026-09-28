using System.Numerics;
using System.Runtime.InteropServices;
using Flyback.Core.Compile;
using Flyback.Core.Render;

namespace Flyback.Gpu;

/// <summary>
/// Draws a patch's frames on the GPU with no window, for a render: the preview's
/// shader through a <see cref="HeadlessContext"/>, read back into memory.
/// </summary>
/// <remarks>
/// Every call must come from the thread that opened it, where the context is current.
/// </remarks>
internal sealed class HeadlessRenderer : IFrameRenderer, IDisposable
{
    private readonly HeadlessContext context;
    private readonly GpuFrameRenderer renderer;
    private byte[] rgba = [];

    private HeadlessRenderer(HeadlessContext context, GpuFrameRenderer renderer)
    {
        this.context = context;
        this.renderer = renderer;
    }

    /// <summary>What drew it, for saying so: the version and the device.</summary>
    public string Description => $"{context.Version} on {context.Renderer}";

    /// <summary>A renderer on this machine's GPU, or null with <paramref name="why"/> saying what stood in the way.</summary>
    public static HeadlessRenderer? Open(out string? why)
    {
        HeadlessContext? context;

        try
        {
            context = HeadlessContext.Open(out why);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            why = ex.Message;
            return null;
        }

        if (context is null) return null;

        var version = context.Version;

        if (!GpuFrameRenderer.CanRun(version))
        {
            why = $"{version} on {context.Renderer} is older than the shader backend needs.";
            context.Dispose();
            return null;
        }

        // A render waits for every link, so the driver's own threads buy nothing.
        var renderer = new GpuFrameRenderer(GpuFrameRenderer.DialectFor(version), backgroundLinks: false);

        if (renderer.Initialise(context.Gl) is { } failure)
        {
            renderer.Dispose(context.Gl);
            context.Dispose();
            why = failure;
            return null;
        }

        why = null;
        return new HeadlessRenderer(context, renderer);
    }

    /// <summary>Builds the patch's shader, so a patch the GPU cannot draw is known before any frame. Null on success.</summary>
    public string? Prepare(CompiledPatch patch) => renderer.SetPatch(context.Gl, patch);

    /// <summary>Clears the feedback history, so the next frame starts from black.</summary>
    public void Reset() => renderer.Rewind();

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

        var gl = context.Gl;
        var row = width * 4;

        if (rgba.Length != row * height) rgba = new byte[row * height];

        // Once per frame, as the preview does: the constants, the pictures and a
        // Scope's trace all change without the shader text changing.
        var failure = renderer.SetPatch(gl, patch)
            ?? renderer.Frame(gl, new SurfaceSize(width, height), time, live, rgba);

        if (failure is not null) throw new InvalidOperationException(failure);

        // OpenGL hands the rows back bottom-up and as RGBA; a picture here is
        // top-down BGRA, opaque as the processor draws it.
        for (var y = 0; y < height; y++)
            Swizzle(rgba.AsSpan((height - 1 - y) * row, row), destination.Slice(y * stride, row));
    }

    /// <summary>RGBA to BGRA with the alpha set, a vector of pixels at a time.</summary>
    private static void Swizzle(ReadOnlySpan<byte> from, Span<byte> to)
    {
        var source = MemoryMarshal.Cast<byte, uint>(from);
        var target = MemoryMarshal.Cast<byte, uint>(to);
        var i = 0;

        if (Vector.IsHardwareAccelerated)
        {
            var sources = MemoryMarshal.Cast<uint, Vector<uint>>(source);
            var targets = MemoryMarshal.Cast<uint, Vector<uint>>(target);

            for (var v = 0; v < sources.Length; v++)
                targets[v] = Swap(sources[v]);

            i = sources.Length * Vector<uint>.Count;
        }

        for (; i < source.Length; i++)
            target[i] = (source[i] & 0x0000FF00u) | ((source[i] >> 16) & 0xFFu) | ((source[i] & 0xFFu) << 16) | 0xFF000000u;
    }

    private static Vector<uint> Swap(Vector<uint> p) =>
        (p & new Vector<uint>(0x0000FF00u))
        | ((p >> 16) & new Vector<uint>(0xFFu))
        | ((p & new Vector<uint>(0xFFu)) << 16)
        | new Vector<uint>(0xFF000000u);

    public void Dispose()
    {
        context.MakeCurrent();
        renderer.Dispose(context.Gl);
        context.Dispose();
    }
}
