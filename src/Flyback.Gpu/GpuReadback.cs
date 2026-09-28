using System.Runtime.InteropServices;
using static Flyback.Gpu.GlConstants;

namespace Flyback.Gpu;

/// <summary>
/// Getting the frame the GPU just drew back into main memory, for a recording or
/// a render. Nothing else in the program reads a pixel off the card.
/// </summary>
/// <remarks>
/// A recording reads through two alternating pixel buffers, which keeps the
/// pipeline moving at the cost of one frame of lag; without mappable buffers the
/// read stalls instead. A render reads straight into its own memory and waits.
/// <para>
/// The float surface is blitted to <c>RGBA8</c> before reading, since ES rejects
/// a float-to-byte read and leaves a black frame. Every read checks
/// <c>glGetError</c>, because the failure is otherwise silent.
/// </para>
/// </remarks>
internal sealed class GpuReadback
{
    private bool mappable;

    private readonly int[] buffers = [0, 0];

    /// <summary>An eight-bit copy of the frame, which is the only thing legal to read as bytes.</summary>
    private int resolveTexture;
    private int resolveFramebuffer;

    private SurfaceSize size;
    private int write;
    private bool primed;

    /// <summary>Where a direct read lands, when there is nowhere better.</summary>
    private IntPtr staging;

    private byte[] pixels = [];

    /// <summary>Why this cannot read frames back, or null when it can.</summary>
    public string? Unavailable { get; private set; } = "The readback has not been set up.";

    /// <summary>
    /// Finds the entry points. Called with the context current, once, alongside
    /// everything else that does not depend on the patch.
    /// </summary>
    public void Initialise(IGl gl)
    {
        if (!gl.IsReadPixelsAvailable)
        {
            Unavailable = "This context will not read pixels back.";
            return;
        }

        // Optional: their absence costs a stall, not the feature.
        mappable = gl.IsMapBufferAvailable;

        Unavailable = null;
    }

    /// <summary>
    /// Reads <paramref name="framebuffer"/> and hands the frame before it to
    /// <paramref name="sink"/>. Leaves no buffer bound.
    /// </summary>
    /// <param name="eightBit">
    /// Whether the frame is already a normalized eight-bit surface. When it is
    /// not, and it cannot be blitted to one, it cannot be read as bytes at all.
    /// </param>
    public void Capture(IGl gl, int framebuffer, SurfaceSize resolution, bool eightBit, IFrameSink sink)
    {
        if (Unavailable is not null) return;
        if (resolution.Width <= 0 || resolution.Height <= 0) return;

        Resize(gl, resolution);

        if (Source(gl, framebuffer, resolution, eightBit) < 0) return;

        if (!mappable || buffers[0] == 0)
        {
            Direct(gl, resolution, sink);
            return;
        }

        var bytes = Bytes(resolution);

        // This frame's read is only issued; the card fills it in its own time.
        gl.BindBuffer(GL_PIXEL_PACK_BUFFER, buffers[write]);
        gl.ReadPixels(0, 0, resolution.Width, resolution.Height, GL_RGBA, GL_UNSIGNED_BYTE, IntPtr.Zero);

        var previous = 1 - write;

        // The frame before it has had a whole frame to arrive, so mapping it is
        // a copy rather than a wait.
        if (primed)
        {
            gl.BindBuffer(GL_PIXEL_PACK_BUFFER, buffers[previous]);
            var mapped = gl.MapBufferRange(GL_PIXEL_PACK_BUFFER, IntPtr.Zero, bytes, GL_MAP_READ_BIT);

            if (mapped != IntPtr.Zero)
            {
                Marshal.Copy(mapped, pixels, 0, (int)bytes);
                gl.UnmapBuffer(GL_PIXEL_PACK_BUFFER);

                sink.Accept(pixels, resolution.Width, resolution.Height);
            }
        }

        gl.BindBuffer(GL_PIXEL_PACK_BUFFER, 0);

        write = previous;
        primed = true;

        Check(gl);
    }

    /// <summary>
    /// Reads <paramref name="framebuffer"/> into <paramref name="rgba"/>, tightly
    /// packed and bottom-up, waiting for the card. Null on success.
    /// </summary>
    public unsafe string? Read(IGl gl, int framebuffer, SurfaceSize resolution, bool eightBit, Span<byte> rgba)
    {
        if (Unavailable is not null) return Unavailable;
        if (rgba.Length < Bytes(resolution)) throw new ArgumentException("The frame does not fit.", nameof(rgba));

        Resize(gl, resolution);

        if (Source(gl, framebuffer, resolution, eightBit) < 0) return Unavailable;

        gl.BindBuffer(GL_PIXEL_PACK_BUFFER, 0);

        fixed (byte* destination = rgba)
            gl.ReadPixels(0, 0, resolution.Width, resolution.Height, GL_RGBA, GL_UNSIGNED_BYTE, (IntPtr)destination);

        return Check(gl) ? null : Unavailable;
    }

    /// <summary>
    /// Binds the eight-bit copy of <paramref name="framebuffer"/> for reading, and
    /// returns it; -1 when there is none, with <see cref="Unavailable"/> saying why.
    /// </summary>
    private int Source(IGl gl, int framebuffer, SurfaceSize resolution, bool eightBit)
    {
        var source = Resolve(gl, framebuffer, resolution, eightBit);

        if (source < 0)
        {
            Unavailable ??= "This GPU keeps its frames as half floats and will not convert them for a read.";
            return -1;
        }

        gl.BindFramebuffer(GL_READ_FRAMEBUFFER, source);

        // One color attachment, so this is already the read buffer — said out
        // loud because a framebuffer arriving from elsewhere might not be.
        if (gl.IsReadBufferAvailable) gl.ReadBuffer(GL_COLOR_ATTACHMENT0);

        return source;
    }

    /// <summary>Straight into memory, waiting for the card to catch up.</summary>
    private void Direct(IGl gl, SurfaceSize resolution, IFrameSink sink)
    {
        if (staging == IntPtr.Zero) return;

        gl.ReadPixels(0, 0, resolution.Width, resolution.Height, GL_RGBA, GL_UNSIGNED_BYTE, staging);

        if (!Check(gl)) return;

        Marshal.Copy(staging, pixels, 0, (int)Bytes(resolution));

        sink.Accept(pixels, resolution.Width, resolution.Height);
    }

    /// <summary>
    /// Whether the read went through. A refused <c>glReadPixels</c> writes
    /// nothing and says nothing, and the buffer it left alone is black — so the
    /// only way to tell a failure from a dark patch is to ask.
    /// </summary>
    private bool Check(IGl gl)
    {
        var error = gl.GetError();

        if (error == GL_NO_ERROR) return true;

        Unavailable = error == GL_INVALID_OPERATION
            ? "This GPU will not hand back a frame in a form that can be written to a file."
            : $"Reading the frame back failed (GL error {error}).";

        return false;
    }

    /// <summary>
    /// Copies the frame into an eight-bit surface, which is the only kind
    /// <c>glReadPixels</c> is obliged to hand back as bytes. Returns the
    /// framebuffer to read from, or -1 when there is no way to get one.
    /// </summary>
    private int Resolve(IGl gl, int framebuffer, SurfaceSize resolution, bool eightBit)
    {
        // Nowhere to convert to: readable directly only if it never needed
        // converting, which is the machine where half floats were refused.
        if (resolveFramebuffer == 0) return eightBit ? framebuffer : -1;

        gl.BindFramebuffer(GL_READ_FRAMEBUFFER, framebuffer);
        gl.BindFramebuffer(GL_DRAW_FRAMEBUFFER, resolveFramebuffer);

        // Same size on both sides, so the filter never comes into it.
        gl.BlitFramebuffer(
            0, 0, resolution.Width, resolution.Height,
            0, 0, resolution.Width, resolution.Height,
            GL_COLOR_BUFFER_BIT, GL_NEAREST);

        return Check(gl) ? resolveFramebuffer : -1;
    }

    private static IntPtr Bytes(SurfaceSize resolution) => new(resolution.Width * resolution.Height * 4L);

    private void Resize(IGl gl, SurfaceSize resolution)
    {
        if (size == resolution && pixels.Length > 0) return;

        Release(gl);

        size = resolution;
        primed = false;
        write = 0;

        var bytes = Bytes(resolution);

        pixels = new byte[(int)bytes];
        staging = Marshal.AllocHGlobal(bytes);

        MakeResolveTarget(gl, resolution);

        if (!mappable) return;

        for (var i = 0; i < 2; i++)
        {
            buffers[i] = gl.GenBuffer();
            gl.BindBuffer(GL_PIXEL_PACK_BUFFER, buffers[i]);

            // Read once, written by the card: exactly what STREAM_READ is for.
            gl.BufferData(GL_PIXEL_PACK_BUFFER, bytes, IntPtr.Zero, GL_STREAM_READ);
        }

        gl.BindBuffer(GL_PIXEL_PACK_BUFFER, 0);
    }

    /// <summary>
    /// The eight-bit surface every read actually comes from. Built here rather
    /// than borrowed from the renderer's history pair, which is half float
    /// wherever the machine allows it and therefore unreadable as bytes.
    /// </summary>
    private void MakeResolveTarget(IGl gl, SurfaceSize resolution)
    {
        if (!gl.IsBlitFramebufferAvailable) return;

        resolveTexture = gl.GenTexture();
        gl.BindTexture(GL_TEXTURE_2D, resolveTexture);
        gl.TexImage2D(
            GL_TEXTURE_2D, 0, GL_RGBA8,
            resolution.Width, resolution.Height, 0,
            GL_RGBA, GL_UNSIGNED_BYTE, IntPtr.Zero);

        // Never sampled, only blitted into and read from, so the filters are
        // set only because a texture without them is incomplete.
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);

        resolveFramebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(GL_FRAMEBUFFER, resolveFramebuffer);
        gl.FramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, resolveTexture, 0);

        if (gl.CheckFramebufferStatus(GL_FRAMEBUFFER) == GL_FRAMEBUFFER_COMPLETE) return;

        // Eight-bit RGBA is the one format every context can render to, so this
        // should not happen — and if it does, a direct read is still worth a try.
        gl.DeleteFramebuffer(resolveFramebuffer);
        gl.DeleteTexture(resolveTexture);

        resolveFramebuffer = 0;
        resolveTexture = 0;
    }

    /// <summary>Hands the buffers back. Safe to call with the objects already gone.</summary>
    public void Release(IGl? gl)
    {
        for (var i = 0; i < 2; i++)
        {
            if (buffers[i] != 0) gl?.DeleteBuffer(buffers[i]);
            buffers[i] = 0;
        }

        if (resolveFramebuffer != 0) gl?.DeleteFramebuffer(resolveFramebuffer);
        if (resolveTexture != 0) gl?.DeleteTexture(resolveTexture);

        resolveFramebuffer = 0;
        resolveTexture = 0;

        if (staging != IntPtr.Zero) Marshal.FreeHGlobal(staging);

        staging = IntPtr.Zero;
        pixels = [];
        size = default;
        primed = false;
    }
}
