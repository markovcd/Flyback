using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.OpenGL;
using Flyback.App.Capture;
using static Avalonia.OpenGL.GlConsts;

namespace Flyback.App.Controls;

/// <summary>
/// Getting the frame the GPU just drew back into main memory, for whoever is
/// recording. Nothing else in the program reads a pixel off the card.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GlInterface"/> carries no <c>glReadPixels</c> — Avalonia binds what
/// Avalonia draws with, and it never reads back — so the three entry points this
/// needs are fetched through <see cref="GlInterface.GetProcAddress"/>. That is
/// the same door Avalonia's own bindings come through, including the GL 1.0 ones
/// it uses every frame, so a context that can draw can be read.
/// </para>
/// <para>
/// Two pixel buffers, alternating. The read is issued into one and the other —
/// last frame's, long since arrived — is mapped and copied. The frame handed on
/// is therefore one behind the screen, which no one can see and which is the
/// whole price of not stopping the pipeline to wait for the card.
/// </para>
/// <para>
/// Where buffers cannot be mapped the read is done straight into memory instead.
/// That does stall, but a stalled recording is better than none, and the caller
/// already fences the patch pass with <c>glFinish</c> every frame regardless.
/// </para>
/// <para>
/// <b>The frame is resolved to eight bits before it is read.</b> The history pair
/// is half-float wherever that is renderable, and reading a float surface as
/// bytes is not a combination <c>glReadPixels</c> is required to accept — ES
/// answers <c>GL_INVALID_OPERATION</c>, writes nothing, and leaves a buffer of
/// zeroes that looks exactly like a patch which drew black. Blitting into an
/// <c>RGBA8</c> target first makes the pair legal whatever the history is, has
/// the card do the conversion, and keeps the transfer at four bytes a pixel
/// rather than sixteen.
/// </para>
/// <para>
/// Every read is followed by <c>glGetError</c>. Silence is the failure mode that
/// matters here: a readback that quietly does nothing produces a file full of
/// black frames and no reason for them.
/// </para>
/// </remarks>
internal sealed class GpuReadback
{
    // None of these are in GlConsts, which carries the ES 2.0 era set.
    private const int GlPixelPackBuffer = 0x88EB;
    private const int GlStreamRead = 0x88E1;
    private const int GlMapReadBit = 0x0001;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlReadPixels(int x, int y, int width, int height, int format, int type, IntPtr pixels);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GlMapBufferRange(int target, IntPtr offset, IntPtr length, int access);

    /// <remarks>
    /// Returns a <c>GLboolean</c>, which is one byte. Declared as one rather than
    /// as <see cref="bool"/>, which the marshaller would take for a four-byte
    /// Win32 <c>BOOL</c> and read three bytes of whatever happened to follow.
    /// </remarks>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate byte GlUnmapBuffer(int target);

    private GlReadPixels? readPixels;
    private GlMapBufferRange? mapBufferRange;
    private GlUnmapBuffer? unmapBuffer;

    private readonly int[] buffers = [0, 0];

    /// <summary>An eight-bit copy of the frame, which is the only thing legal to read as bytes.</summary>
    private int resolveTexture;
    private int resolveFramebuffer;

    private PixelSize size;
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
    public void Initialise(GlInterface gl)
    {
        readPixels = Bind<GlReadPixels>(gl, "glReadPixels");

        if (readPixels is null)
        {
            Unavailable = "This context will not read pixels back.";
            return;
        }

        // Optional: their absence costs a stall, not the feature.
        mapBufferRange = Bind<GlMapBufferRange>(gl, "glMapBufferRange");
        unmapBuffer = Bind<GlUnmapBuffer>(gl, "glUnmapBuffer");

        Unavailable = null;
    }

    private static T? Bind<T>(GlInterface gl, string name) where T : Delegate
    {
        var address = gl.GetProcAddress(name);

        return address == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    /// <summary>
    /// Reads <paramref name="framebuffer"/> and hands the frame before it to
    /// <paramref name="sink"/>. Leaves no buffer bound.
    /// </summary>
    /// <param name="eightBit">
    /// Whether the frame is already a normalised eight-bit surface. When it is
    /// not, and it cannot be blitted to one, it cannot be read as bytes at all.
    /// </param>
    public void Capture(GlInterface gl, int framebuffer, PixelSize resolution, bool eightBit, IFrameSink sink)
    {
        if (readPixels is null || Unavailable is not null) return;
        if (resolution.Width <= 0 || resolution.Height <= 0) return;

        Resize(gl, resolution);

        var source = Resolve(gl, framebuffer, resolution, eightBit);

        if (source < 0)
        {
            Unavailable ??= "This GPU keeps its frames as half floats and will not convert them for a read.";
            return;
        }

        gl.BindFramebuffer(GL_READ_FRAMEBUFFER, source);

        // One colour attachment, so this is already the read buffer — said out
        // loud because a framebuffer arriving from elsewhere might not be.
        if (gl.IsReadBufferAvailable) gl.ReadBuffer(GL_COLOR_ATTACHMENT0);

        if (mapBufferRange is null || unmapBuffer is null || buffers[0] == 0)
        {
            Direct(gl, resolution, sink);
            return;
        }

        var bytes = Bytes(resolution);

        // This frame's read is only issued; the card fills it in its own time.
        gl.BindBuffer(GlPixelPackBuffer, buffers[write]);
        readPixels(0, 0, resolution.Width, resolution.Height, GL_RGBA, GL_UNSIGNED_BYTE, IntPtr.Zero);

        var previous = 1 - write;

        // The frame before it has had a whole frame to arrive, so mapping it is
        // a copy rather than a wait.
        if (primed)
        {
            gl.BindBuffer(GlPixelPackBuffer, buffers[previous]);
            var mapped = mapBufferRange(GlPixelPackBuffer, IntPtr.Zero, bytes, GlMapReadBit);

            if (mapped != IntPtr.Zero)
            {
                Marshal.Copy(mapped, pixels, 0, (int)bytes);
                unmapBuffer(GlPixelPackBuffer);

                sink.Accept(pixels, resolution.Width, resolution.Height);
            }
        }

        gl.BindBuffer(GlPixelPackBuffer, 0);

        write = previous;
        primed = true;

        Check(gl);
    }

    /// <summary>Straight into memory, waiting for the card to catch up.</summary>
    private void Direct(GlInterface gl, PixelSize resolution, IFrameSink sink)
    {
        if (staging == IntPtr.Zero) return;

        readPixels!(0, 0, resolution.Width, resolution.Height, GL_RGBA, GL_UNSIGNED_BYTE, staging);

        if (!Check(gl)) return;

        Marshal.Copy(staging, pixels, 0, (int)Bytes(resolution));

        sink.Accept(pixels, resolution.Width, resolution.Height);
    }

    /// <summary>
    /// Whether the read went through. A refused <c>glReadPixels</c> writes
    /// nothing and says nothing, and the buffer it left alone is black — so the
    /// only way to tell a failure from a dark patch is to ask.
    /// </summary>
    private bool Check(GlInterface gl)
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
    private int Resolve(GlInterface gl, int framebuffer, PixelSize resolution, bool eightBit)
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

    private static IntPtr Bytes(PixelSize resolution) => new(resolution.Width * resolution.Height * 4L);

    private void Resize(GlInterface gl, PixelSize resolution)
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

        if (mapBufferRange is null || unmapBuffer is null) return;

        for (var i = 0; i < 2; i++)
        {
            buffers[i] = gl.GenBuffer();
            gl.BindBuffer(GlPixelPackBuffer, buffers[i]);

            // Read once, written by the card: exactly what STREAM_READ is for.
            gl.BufferData(GlPixelPackBuffer, bytes, IntPtr.Zero, GlStreamRead);
        }

        gl.BindBuffer(GlPixelPackBuffer, 0);
    }

    /// <summary>
    /// The eight-bit surface every read actually comes from. Built here rather
    /// than borrowed from the renderer's history pair, which is half float
    /// wherever the machine allows it and therefore unreadable as bytes.
    /// </summary>
    private void MakeResolveTarget(GlInterface gl, PixelSize resolution)
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
    public void Release(GlInterface? gl)
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
