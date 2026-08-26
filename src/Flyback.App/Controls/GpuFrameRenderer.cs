using Avalonia;
using Avalonia.OpenGL;
using Flyback.App.Capture;
using Flyback.Core.Compile;
using static Avalonia.OpenGL.GlConsts;

namespace Flyback.App.Controls;

/// <summary>
/// Everything this project asks of OpenGL, in one place. It owns the two shader
/// programs, the pair of textures the feedback history ping-pongs between, and
/// the single vertex array a triangle needs — and nothing above it touches a GL
/// call.
/// </summary>
/// <remarks>
/// <para>
/// Every method here must be called with the context current, which in practice
/// means from inside <see cref="Avalonia.OpenGL.Controls.OpenGlControlBase"/>'s
/// init, render and deinit callbacks and from nowhere else.
/// </para>
/// <para>
/// The methods return an error string rather than throwing. A shader that will
/// not compile is not exceptional — it is a machine this backend cannot run on,
/// and the answer to it is to say so once and hand the frame back to the CPU.
/// </para>
/// </remarks>
internal sealed class GpuFrameRenderer(GlslDialect dialect)
{
    // Not in GlConsts, which carries the ES 2.0 era set.
    private const int GlRgba16F = 0x881A;
    private const int GlHalfFloat = 0x140B;
    private const int GlBlend = 0x0BE2;
    private const int GlTriangleStrip = 0x0005;

    /// <summary>The four corners of the unit square, in strip order.</summary>
    private static readonly IntPtr Quad = new(4);

    private int vertexArray;

    private int blitProgram;
    private int blitTexture = -1;
    private int blitScaleX = -1;
    private int blitScaleY = -1;

    private int patchProgram;
    private int patchTime = -1;
    private int patchAspect = -1;
    private int patchPrevious = -1;
    private int patchFeedbackX = -1;
    private int patchFeedbackY = -1;
    private int[] patchConstants = [];
    private float[] constants = [];

    /// <summary>
    /// Where <c>uLive</c>'s elements are, and somewhere to lay a block out before
    /// uploading it. Kept between frames rather than allocated per one: this runs
    /// sixty times a second and the length only changes with the program.
    /// </summary>
    private int[] patchLive = [];
    private float[] played = [];
    private bool usesFeedback;

    /// <summary>
    /// What is on the GPU now. Comparing sources rather than patches is what
    /// keeps a knob drag from recompiling: ADR-0021 rebuilds the whole program on
    /// every edit, but the constants are uniforms, so turning a knob produces a
    /// new <see cref="CompiledPatch"/> whose shader text is identical.
    /// </summary>
    private string liveSource = string.Empty;

    /// <summary>
    /// A source that failed, so it is never tried twice. It will fail again in
    /// exactly the same way, and retrying every frame is a stutter rather than a
    /// recovery.
    /// </summary>
    private string refusedSource = string.Empty;

    private readonly int[] textures = [0, 0];
    private readonly int[] framebuffers = [0, 0];
    private readonly GpuReadback readback = new();
    private PixelSize size;
    private int read;
    private bool clearPending = true;

    /// <summary>
    /// Who wants the frames, if anybody does. Set from the UI thread and read on
    /// the render thread, which is a reference and so is atomic either way; a
    /// recording that starts one frame late is not a thing anyone can perceive.
    /// </summary>
    public IFrameSink? Capture { get; set; }

    /// <summary>Why frames cannot be read back here, or null when they can.</summary>
    public string? CaptureUnavailable => readback.Unavailable;

    /// <summary>
    /// True when the frame history had to fall back to eight bits per channel
    /// because half floats were not renderable here. A feedback loop then
    /// posterises, exactly as ADR-0012 describes it would.
    /// </summary>
    public bool EightBitFeedback { get; private set; }

    /// <summary>
    /// What the patch shader itself cost, fenced so it is the drawing rather than
    /// the asking.
    /// </summary>
    /// <remarks>
    /// Timed around the offscreen pass alone, and deliberately not around the
    /// blit. The blit feeds the compositor, and a patch that samples its own last
    /// frame makes this frame wait on the previous one having been presented — so
    /// a fence after the blit reads one refresh interval whatever the shader cost,
    /// and would report a fast patch as a slow one. The number this reports means
    /// the same thing SynthRenderer's does: what it took to work out the picture.
    /// </remarks>
    public double PatchMilliseconds { get; private set; }

    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>The dialect a context of this version speaks, and whether it is new enough at all.</summary>
    public static bool CanRun(GlVersion version) => version.Type == GlProfileType.OpenGLES
        ? version.Major >= 3
        : version.Major > 3 || (version.Major == 3 && version.Minor >= 2);

    public static GlslDialect DialectFor(GlVersion version) =>
        version.Type == GlProfileType.OpenGLES ? GlslDialect.GlslEs300 : GlslDialect.Glsl150;

    /// <summary>Builds the parts that do not depend on the patch. Null on success.</summary>
    public string? Initialise(GlInterface gl)
    {
        // A core-profile context refuses to draw without one, and this backend
        // has no vertex data to put in it — the triangle comes from gl_VertexID.
        if (!gl.IsGenVertexArraysAvailable || !gl.IsBindVertexArrayAvailable)
            return "This context has no vertex arrays.";

        vertexArray = gl.GenVertexArray();

        var shaders = GlslEmitter.Emit(CompiledPatch.Black, dialect);
        var program = Link(gl, shaders.BlitVertex, shaders.BlitFragment, out var error);
        if (program is not { } blit) return $"The blit shader would not build. {error}";

        blitProgram = blit;
        blitTexture = gl.GetUniformLocationString(blit, "uTexture");
        blitScaleX = gl.GetUniformLocationString(blit, "uScaleX");
        blitScaleY = gl.GetUniformLocationString(blit, "uScaleY");

        // Not fatal when it fails: a machine that cannot read frames back can
        // still show them, and only a recording is refused.
        readback.Initialise(gl);

        return null;
    }

    /// <summary>
    /// Points the renderer at a program, compiling a shader for it if the text
    /// has actually changed. Null on success — including the very common case
    /// where there was nothing to do.
    /// </summary>
    public string? SetPatch(GlInterface gl, CompiledPatch patch)
    {
        var shaders = GlslEmitter.Emit(patch, dialect);

        // The values behind the constants change with every knob; where they sit
        // does not, so this is picked up whether or not the shader is rebuilt.
        constants = GlslEmitter.Constants(patch);

        if (shaders.PatchFragment == liveSource) return null;
        if (shaders.PatchFragment == refusedSource) return null;

        var program = Link(gl, shaders.PatchVertex, shaders.PatchFragment, out var error);

        if (program is not { } compiled)
        {
            refusedSource = shaders.PatchFragment;
            return $"The patch would not compile as a shader. {error}";
        }

        if (patchProgram != 0) gl.DeleteProgram(patchProgram);

        patchProgram = compiled;
        liveSource = shaders.PatchFragment;
        usesFeedback = shaders.UsesFeedback;

        patchTime = gl.GetUniformLocationString(compiled, "uTime");
        patchAspect = gl.GetUniformLocationString(compiled, "uAspect");
        patchPrevious = gl.GetUniformLocationString(compiled, "uPrevious");
        patchFeedbackX = gl.GetUniformLocationString(compiled, "uFeedbackScaleX");
        patchFeedbackY = gl.GetUniformLocationString(compiled, "uFeedbackScaleY");

        // There is no glUniform1fv here, so each element of the array is found
        // and set on its own. There are a few dozen of them in a real patch.
        patchConstants = new int[shaders.ConstantCount];
        for (var i = 0; i < patchConstants.Length; i++)
            patchConstants[i] = gl.GetUniformLocationString(compiled, $"uK[{i}]");

        // The same, for what the patch is being played with. Where the constants
        // are uploaded from a snapshot taken when the program was set, these are
        // read fresh every frame — a knob changes with the patch and a key
        // changes while you are looking at it.
        patchLive = new int[shaders.LiveCount];
        played = new float[shaders.LiveCount];

        for (var i = 0; i < patchLive.Length; i++)
            patchLive[i] = gl.GetUniformLocationString(compiled, $"uLive[{i}]");

        return null;
    }

    /// <summary>Clears the feedback history, so the next frame starts from black.</summary>
    public void Rewind() => clearPending = true;

    /// <summary>
    /// Renders one frame at <paramref name="resolution"/> and draws it,
    /// letterboxed, into <paramref name="framebuffer"/>. Null on success.
    /// </summary>
    public string? Render(
        GlInterface gl,
        int framebuffer,
        PixelSize control,
        PixelSize resolution,
        double time,
        LiveValues? live = null)
    {
        if (patchProgram == 0) return null;
        if (resolution.Width <= 0 || resolution.Height <= 0) return null;

        if (Resize(gl, resolution) is { } failure) return failure;

        // Nothing here draws over anything, so every test that could discard a
        // fragment is off. Skia sets its own state when it gets the context back.
        gl.Disable(GL_DEPTH_TEST);
        gl.Disable(GL_CULL_FACE);
        gl.Disable(GL_SCISSOR_TEST);
        gl.Disable(GlBlend);

        gl.BindVertexArray(vertexArray);

        if (clearPending)
        {
            for (var i = 0; i < 2; i++)
            {
                gl.BindFramebuffer(GL_FRAMEBUFFER, framebuffers[i]);
                gl.ClearColor(0f, 0f, 0f, 1f);
                gl.Clear(GL_COLOR_BUFFER_BIT);
            }

            clearPending = false;
        }

        var started = clock.Elapsed;
        DrawPatch(gl, resolution, time, live);
        gl.Finish();
        PatchMilliseconds = (clock.Elapsed - started).TotalMilliseconds;

        // The frame just drawn becomes the one the next frame reads back.
        read = 1 - read;

        // Before the blit, while the frame is still the whole picture rather than
        // a letterboxed corner of a control. A recording wants what the patch
        // drew, not what the window happened to be shaped like.
        if (Capture is { } sink) readback.Capture(gl, framebuffers[read], resolution, EightBitFeedback, sink);

        DrawBlit(gl, framebuffer, control, resolution);

        // Leaving the context as it was found, apart from the state above. The
        // framebuffer especially: Avalonia hands us one and expects it back.
        gl.BindVertexArray(0);
        gl.BindTexture(GL_TEXTURE_2D, 0);
        gl.BindFramebuffer(GL_FRAMEBUFFER, framebuffer);
        gl.UseProgram(0);

        return null;
    }

    private void DrawPatch(GlInterface gl, PixelSize resolution, double time, LiveValues? live)
    {
        gl.BindFramebuffer(GL_FRAMEBUFFER, framebuffers[1 - read]);
        gl.Viewport(0, 0, resolution.Width, resolution.Height);
        gl.UseProgram(patchProgram);

        if (patchTime >= 0) gl.Uniform1f(patchTime, (float)time);

        var aspect = resolution.Height == 0 ? 1f : (float)resolution.Width / resolution.Height;
        if (patchAspect >= 0) gl.Uniform1f(patchAspect, aspect);

        for (var i = 0; i < patchConstants.Length && i < constants.Length; i++)
            if (patchConstants[i] >= 0)
                gl.Uniform1f(patchConstants[i], constants[i]);

        // Copied out of the block before any of it is uploaded, so the whole
        // frame is drawn with one reading of the keys. Read element by element
        // while the driver was being called, a note landing mid-upload could put
        // a new pitch in the picture beside the old gate.
        if (patchLive.Length > 0)
        {
            if (live is null) Array.Clear(played);
            else live.CopyTo(played);

            for (var i = 0; i < patchLive.Length && i < played.Length; i++)
                if (patchLive[i] >= 0)
                    gl.Uniform1f(patchLive[i], played[i]);
        }

        if (usesFeedback)
        {
            gl.ActiveTexture(GL_TEXTURE0);
            gl.BindTexture(GL_TEXTURE_2D, textures[read]);
            if (patchPrevious >= 0) gl.Uniform1i(patchPrevious, 0);

            // The whole of CompiledPatch.Sample's mapping, reduced to two scales:
            // patch coordinates to texel centres, and the flip between a picture
            // indexed downwards and a texture stored upwards. The offset is 0.5
            // on both axes and so is baked into the shader.
            if (patchFeedbackX >= 0)
                gl.Uniform1f(patchFeedbackX, 0.5f * (resolution.Width - 1) / (resolution.Width * aspect));

            if (patchFeedbackY >= 0)
                gl.Uniform1f(patchFeedbackY, 0.5f * (resolution.Height - 1) / resolution.Height);
        }

        gl.DrawArrays(GlTriangleStrip, 0, Quad);
    }

    private void DrawBlit(GlInterface gl, int framebuffer, PixelSize control, PixelSize resolution)
    {
        gl.BindFramebuffer(GL_FRAMEBUFFER, framebuffer);
        gl.Viewport(0, 0, control.Width, control.Height);

        // The black behind a letterboxed picture, which the CPU surface paints
        // with a filled rectangle for the same reason.
        gl.ClearColor(0f, 0f, 0f, 1f);
        gl.Clear(GL_COLOR_BUFFER_BIT);

        gl.UseProgram(blitProgram);
        gl.ActiveTexture(GL_TEXTURE0);
        gl.BindTexture(GL_TEXTURE_2D, textures[read]);

        if (blitTexture >= 0) gl.Uniform1i(blitTexture, 0);

        var (scaleX, scaleY) = Letterbox(control, resolution);
        if (blitScaleX >= 0) gl.Uniform1f(blitScaleX, scaleX);
        if (blitScaleY >= 0) gl.Uniform1f(blitScaleY, scaleY);

        gl.DrawArrays(GlTriangleStrip, 0, Quad);
    }

    /// <summary>
    /// The largest rectangle of the picture's aspect that fits in the control, as
    /// a fraction of the control on each axis. Scaling about the origin in clip
    /// space centres it, so there is no offset to carry.
    /// </summary>
    private static (float X, float Y) Letterbox(PixelSize control, PixelSize image)
    {
        if (control.Width <= 0 || control.Height <= 0 || image.Width <= 0 || image.Height <= 0)
            return (1f, 1f);

        var scale = Math.Min(
            (float)control.Width / image.Width,
            (float)control.Height / image.Height);

        return (image.Width * scale / control.Width, image.Height * scale / control.Height);
    }

    /// <summary>
    /// Allocates the history pair, half floats first. The history does not
    /// survive a resolution change, which is what the CPU renderer does too.
    /// </summary>
    private string? Resize(GlInterface gl, PixelSize resolution)
    {
        if (size == resolution && framebuffers[0] != 0) return null;

        Release(gl);
        size = resolution;
        clearPending = true;

        // Half floats keep a feedback loop off the eight-bit ladder ADR-0012 is
        // about. They are not renderable everywhere, and where they are not, a
        // posterised feedback is still a picture.
        foreach (var (internalFormat, type, eightBit) in
                 (ReadOnlySpan<(int, int, bool)>)[(GlRgba16F, GlHalfFloat, false), (GL_RGBA8, GL_UNSIGNED_BYTE, true)])
        {
            var complete = true;

            for (var i = 0; i < 2; i++)
            {
                textures[i] = gl.GenTexture();
                gl.BindTexture(GL_TEXTURE_2D, textures[i]);
                gl.TexImage2D(
                    GL_TEXTURE_2D, 0, internalFormat,
                    resolution.Width, resolution.Height, 0,
                    GL_RGBA, type, IntPtr.Zero);

                // Linear filtering is the bilinear read Sample does by hand, and
                // clamping to the edge is its clamp to the last row and column.
                gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
                gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
                gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
                gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

                framebuffers[i] = gl.GenFramebuffer();
                gl.BindFramebuffer(GL_FRAMEBUFFER, framebuffers[i]);
                gl.FramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, textures[i], 0);

                complete &= gl.CheckFramebufferStatus(GL_FRAMEBUFFER) == GL_FRAMEBUFFER_COMPLETE;
            }

            if (complete)
            {
                EightBitFeedback = eightBit;
                return null;
            }

            Release(gl);
        }

        return "This GPU will not render to an offscreen buffer.";
    }

    private static int? Link(GlInterface gl, string vertex, string fragment, out string? error)
    {
        var vertexShader = gl.CreateShader(GL_VERTEX_SHADER);
        error = gl.CompileShaderAndGetError(vertexShader, vertex);

        if (error is not null)
        {
            gl.DeleteShader(vertexShader);
            return null;
        }

        var fragmentShader = gl.CreateShader(GL_FRAGMENT_SHADER);
        error = gl.CompileShaderAndGetError(fragmentShader, fragment);

        if (error is not null)
        {
            gl.DeleteShader(vertexShader);
            gl.DeleteShader(fragmentShader);
            return null;
        }

        var program = gl.CreateProgram();
        gl.AttachShader(program, vertexShader);
        gl.AttachShader(program, fragmentShader);
        error = gl.LinkProgramAndGetError(program);

        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);

        if (error is null) return program;

        gl.DeleteProgram(program);
        return null;
    }

    private void Release(GlInterface gl)
    {
        for (var i = 0; i < 2; i++)
        {
            if (framebuffers[i] != 0) gl.DeleteFramebuffer(framebuffers[i]);
            if (textures[i] != 0) gl.DeleteTexture(textures[i]);
            framebuffers[i] = 0;
            textures[i] = 0;
        }
    }

    /// <summary>
    /// Hands everything back. Also the state to return to after the context is
    /// lost, where the objects are already gone and the names mean nothing.
    /// </summary>
    public void Dispose(GlInterface? gl)
    {
        // Unconditionally, because it owns unmanaged memory as well as GL names
        // and that has to go back whether or not there is still a context.
        readback.Release(gl);

        if (gl is not null)
        {
            Release(gl);

            if (patchProgram != 0) gl.DeleteProgram(patchProgram);
            if (blitProgram != 0) gl.DeleteProgram(blitProgram);
            if (vertexArray != 0) gl.DeleteVertexArray(vertexArray);
        }

        Array.Clear(framebuffers);
        Array.Clear(textures);
        patchProgram = 0;
        blitProgram = 0;
        vertexArray = 0;
        size = default;
        liveSource = string.Empty;
        clearPending = true;
    }
}
