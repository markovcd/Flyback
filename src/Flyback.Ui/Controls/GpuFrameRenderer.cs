using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.OpenGL;
using Flyback.App.Capture;
using Flyback.Core.Compile;
using static Avalonia.OpenGL.GlConsts;

namespace Flyback.App.Controls;

/// <summary>
/// Everything this project asks of OpenGL, in one place: the two shader programs,
/// the pair of textures the feedback history ping-pongs between, and the single
/// vertex array a triangle needs.
/// </summary>
/// <remarks>
/// Every method here must be called with the context current, which means from
/// inside <see cref="Avalonia.OpenGL.Controls.OpenGlControlBase"/>'s callbacks and
/// nowhere else. They return an error string rather than throwing: a shader that
/// will not compile is a machine this backend cannot run on, and the answer is to
/// say so once and hand the frame back to the CPU.
/// </remarks>
internal sealed class GpuFrameRenderer(GlslDialect dialect)
{
    // Not in GlConsts, which carries the ES 2.0 era set.
    private const int GlRgba16F = 0x881A;
    private const int GlRgba32F = 0x8814;
    private const int GlHalfFloat = 0x140B;
    private const int GlBlend = 0x0BE2;
    private const int GlTriangleStrip = 0x0005;

    /// <remarks>
    /// <see cref="GlInterface"/> binds what Avalonia draws with, and Avalonia
    /// draws into one attachment — so the call that turns the others on comes
    /// through <see cref="GlInterface.GetProcAddress"/>, the door
    /// <see cref="GpuReadback"/> uses for the same reason.
    /// </remarks>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlDrawBuffers(int count, IntPtr buffers);

    /// <remarks>
    /// Desktop GLSL 1.50 has no <c>layout(location = …)</c> on a fragment output,
    /// so which attachment each one goes to is said from out here instead. ES has
    /// the qualifier and not this call, which is why the shader carries both — see
    /// <see cref="GlslEmitter"/>.
    /// </remarks>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlBindFragDataLocation(int program, int color, string name);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlGetIntegerv(int name, out int value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GlGetStringi(int name, int index);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlMaxShaderCompilerThreads(uint count);

    private GlDrawBuffers? drawBuffers;
    private GlBindFragDataLocation? bindFragDataLocation;

    // KHR_parallel_shader_compile, and what it takes to ask for it.
    private const int GlCompletionStatus = 0x91B1;
    private const int GlNumExtensions = 0x821D;
    private const int GlExtensions = 0x1F03;
    private const int GlCompileStatus = 0x8B81;
    private const int GlLinkStatus = 0x8B82;

    /// <summary>
    /// Whether the driver builds a shader on threads of its own. A large patch's
    /// shader takes seconds to link, and a link waited for here stops the render
    /// thread, which draws the whole window.
    /// </summary>
    private bool parallel;

    /// <summary>
    /// A patch's shader the driver is still building. The program before it goes
    /// on drawing meanwhile, with the constants, pictures and planes it was built
    /// for; the new patch's take over only when its shader does.
    /// </summary>
    private Building? building;

    private sealed record Building(int Program, int Vertex, int Fragment, ShaderSource Shaders);

    /// <summary>Whether a shader is still being built.</summary>
    public bool Linking => building is not null;

    /// <summary>
    /// Whether the patch waiting on that shader has just been opened, so the one
    /// before it holds its last frame rather than playing on beside a new file.
    /// </summary>
    private bool opening;

    /// <summary>The time the last frame was drawn at, or NaN before the first.</summary>
    private double drawnAt = double.NaN;

    /// <summary>The four corners of the unit square, in strip order.</summary>
    private static readonly IntPtr Quad = new(4);

    private int vertexArray;

    private int blitProgram;
    private int blitTexture = -1;
    private int blitScaleX = -1;
    private int blitScaleY = -1;

    private int patchProgram;
    private int patchTime = -1;

    /// <summary>What the clock is past the float <see cref="patchTime"/> rounds it to.</summary>
    private int patchTimeLow = -1;

    /// <summary>A 1 the shader compiler cannot see, which keeps its two-float sums exact.</summary>
    private int patchOne = -1;
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

    /// <summary>The live inputs of the program on the card, in the order <c>uLive</c> numbers them.</summary>
    private IReadOnlyList<string> liveInputs = [];

    private bool usesFeedback;

    /// <summary>The textures the patch's pictures are in, and which pictures those are.</summary>
    private int[] pictures = [];
    private LoadedImage[] shown = [];
    private int[] patchPictures = [];
    private int[] patchPictureAspects = [];

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

    /// <summary>
    /// The planes the patch carries from frame to frame, four to a texture and
    /// ping-ponged beside the history: a shader cannot read the target it is
    /// writing, which is the one thing the processor's planes do not have to care
    /// about. Empty for a patch with no loop in it — see ADR-0074.
    /// </summary>
    private readonly int[][] planes = [[], []];

    /// <summary>How many of those the program on the card wants, and where its samplers are.</summary>
    private int planeTargets;

    private int[] patchPlanes = [];
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

        // Neither of these is fatal here either: a context without them draws
        // every patch that has no loop in it, and a patch that has one is refused
        // in SetPatch and drawn on the processor instead.
        drawBuffers = Bind<GlDrawBuffers>(gl, "glDrawBuffers");
        bindFragDataLocation = Bind<GlBindFragDataLocation>(gl, "glBindFragDataLocation");

        parallel = Supports(gl, "GL_KHR_parallel_shader_compile") || Supports(gl, "GL_ARB_parallel_shader_compile");

        // As many threads as it likes; left alone, a driver may choose none.
        if (parallel)
            (Bind<GlMaxShaderCompilerThreads>(gl, "glMaxShaderCompilerThreadsKHR")
                ?? Bind<GlMaxShaderCompilerThreads>(gl, "glMaxShaderCompilerThreadsARB"))?.Invoke(uint.MaxValue);

        // Not fatal when it fails: a machine that cannot read frames back can
        // still show them, and only a recording is refused.
        readback.Initialise(gl);

        return null;
    }

    private static T? Bind<T>(GlInterface gl, string name) where T : Delegate
    {
        var address = gl.GetProcAddress(name);

        return address == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static bool Supports(GlInterface gl, string extension)
    {
        if (Bind<GlGetIntegerv>(gl, "glGetIntegerv") is not { } getIntegerv
            || Bind<GlGetStringi>(gl, "glGetStringi") is not { } getStringi)
            return false;

        getIntegerv(GlNumExtensions, out var count);

        for (var i = 0; i < count; i++)
            if (Marshal.PtrToStringAnsi(getStringi(GlExtensions, i)) == extension)
                return true;

        return false;
    }

    /// <summary>
    /// Points the renderer at a program, compiling a shader for it if the text
    /// has actually changed. Null on success — including the very common case
    /// where there was nothing to do.
    /// </summary>
    public string? SetPatch(GlInterface gl, CompiledPatch patch)
    {
        var shaders = GlslEmitter.Emit(patch, dialect);

        // A patch that carries something round per pixel needs a target per four
        // planes, which needs the call that turns the extra attachments on. Where
        // there is none, saying so hands the frame to the processor, which draws
        // the same picture more slowly.
        if (shaders.PlaneTargets > 0 && drawBuffers is null)
            return "This context draws to one attachment, and a loop in the patch needs several.";

        // Where the text cannot say which attachment an output goes to, something
        // has to, and a linker left to choose would put the planes wherever it
        // liked — which draws a picture rather than an error.
        if (shaders.PlaneTargets > 0 && dialect is GlslDialect.Glsl150 && bindFragDataLocation is null)
            return "This context cannot be told where a shader's outputs go, and a loop in the patch needs to say.";

        if (shaders.PatchFragment == refusedSource)
        {
            Abandon(gl);
            return null;
        }

        if (shaders.PatchFragment == liveSource)
        {
            Abandon(gl);
            Adopt(gl, patch);
            return null;
        }

        if (building?.Shaders.PatchFragment != shaders.PatchFragment)
        {
            Abandon(gl);
            building = Start(gl, shaders);
        }

        opening = patch.Waiting;

        // Asked without waiting; the program on the card draws until it is done.
        if (parallel && ProgramParameter(gl, building.Program, GlCompletionStatus) == 0) return null;

        var program = Finish(gl, building, out var error);
        building = null;

        if (program is not { } compiled)
        {
            refusedSource = shaders.PatchFragment;
            return $"The patch would not compile as a shader. {error}";
        }

        if (patchProgram != 0) gl.DeleteProgram(patchProgram);

        patchProgram = compiled;
        liveSource = shaders.PatchFragment;
        usesFeedback = shaders.UsesFeedback;

        // The attachments belong to the framebuffers, so a patch that gained or
        // lost a loop is a reason to build them again. Forgetting the size is how
        // that is asked for; Resize is the only thing that reads it.
        if (shaders.PlaneTargets != planeTargets)
        {
            planeTargets = shaders.PlaneTargets;
            size = default;
        }

        Adopt(gl, patch);

        patchTime = gl.GetUniformLocationString(compiled, "uTime");
        patchTimeLow = gl.GetUniformLocationString(compiled, "uTimeLo");
        patchOne = gl.GetUniformLocationString(compiled, "uOne");
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

        // One sampler and one shape per picture the patch shows. Their locations
        // belong to the program and are found here; the pictures behind them do
        // not, and are uploaded above — a patch that swaps one photograph for
        // another of the same shape emits identical text and rebuilds nothing.
        patchPictures = new int[shaders.PictureCount];
        patchPictureAspects = new int[shaders.PictureCount];

        for (var i = 0; i < patchPictures.Length; i++)
        {
            patchPictures[i] = gl.GetUniformLocationString(compiled, $"uPicture{i}");
            patchPictureAspects[i] = gl.GetUniformLocationString(compiled, $"uPictureAspect{i}");
        }

        // And one sampler per plane target, holding what the frame before left in
        // it. What is bound to them is chosen per frame, since which of the pair
        // is being read alternates.
        patchPlanes = new int[shaders.PlaneTargets];

        for (var i = 0; i < patchPlanes.Length; i++)
            patchPlanes[i] = gl.GetUniformLocationString(compiled, $"uPlane{i}");

        return null;
    }

    /// <summary>
    /// Takes up what a patch hands the program on the card beside its text: the
    /// values behind its constants, which move with every knob, its pictures,
    /// which change without the text changing, and the names of its live inputs.
    /// </summary>
    private void Adopt(GlInterface gl, CompiledPatch patch)
    {
        constants = GlslEmitter.Constants(patch);
        liveInputs = patch.LiveInputs;
        Upload(gl, patch.Pictures);
    }

    /// <summary>
    /// Puts the patch's pictures on the GPU, and takes down whatever was there
    /// before. One texture each, in the order the program names them.
    /// </summary>
    /// <remarks>
    /// Keyed on the pictures rather than the shader text, because the two do not
    /// change together: a different photograph of the same shape is the same
    /// program, and a knob turn is a different program with the same picture. The
    /// library hands back the same <see cref="LoadedImage"/> for a path it has read,
    /// so the comparison is by reference and a knob drag uploads nothing.
    /// Eight-bit textures with linear filtering, which is what the file held.
    /// </remarks>
    private void Upload(GlInterface gl, IReadOnlyList<LoadedImage> wanted)
    {
        if (pictures.Length == wanted.Count)
        {
            var same = true;
            for (var i = 0; i < pictures.Length; i++) same &= ReferenceEquals(shown[i], wanted[i]);

            if (same) return;
        }

        foreach (var texture in pictures) gl.DeleteTexture(texture);

        pictures = new int[wanted.Count];
        shown = [.. wanted];

        for (var i = 0; i < wanted.Count; i++)
        {
            var picture = wanted[i];
            var bytes = new byte[picture.Width * picture.Height * 4];

            for (var pixel = 0; pixel < picture.Width * picture.Height; pixel++)
            {
                bytes[pixel * 4 + 0] = Byte(picture.Pixels[pixel * 3 + 0]);
                bytes[pixel * 4 + 1] = Byte(picture.Pixels[pixel * 3 + 1]);
                bytes[pixel * 4 + 2] = Byte(picture.Pixels[pixel * 3 + 2]);
                bytes[pixel * 4 + 3] = 255;
            }

            pictures[i] = gl.GenTexture();
            gl.BindTexture(GL_TEXTURE_2D, pictures[i]);

            var pinned = GCHandle.Alloc(
                bytes, GCHandleType.Pinned);

            try
            {
                gl.TexImage2D(
                    GL_TEXTURE_2D, 0, GL_RGBA8,
                    picture.Width, picture.Height, 0,
                    GL_RGBA, GL_UNSIGNED_BYTE, pinned.AddrOfPinnedObject());
            }
            finally
            {
                pinned.Free();
            }

            // Linear is the bilinear read the interpreter does by hand, and
            // clamping is what it needs at the last row and column — everything
            // outside the picture is refused by the shader before it gets here,
            // so the clamp is never seen.
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        }

        gl.BindTexture(GL_TEXTURE_2D, 0);
    }

    /// <summary>The eight bits a picture was read from, put back.</summary>
    private static byte Byte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

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
        if (patchProgram == 0 && building is null) return null;
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

                // Opaque black where the picture is all there is, and transparent
                // black where the planes are attached beside it: the clear reaches
                // every attachment, and a plane that took the picture's alpha
                // would start every loop holding one rather than nothing.
                gl.ClearColor(0f, 0f, 0f, planeTargets == 0 ? 1f : 0f);
                gl.Clear(GL_COLOR_BUFFER_BIT);
            }

            clearPending = false;
        }

        // While a shader is built, frames come whatever the clock says, and a loop
        // drawn again at the same time would run on while paused. Black before the
        // first program, and a new file's picture starts with its sound.
        if (patchProgram != 0 && (building is null || (!opening && time != drawnAt)))
        {
            DrawPatch(gl, resolution, time, live);
            drawnAt = time;

            // The frame just drawn becomes the one the next frame reads back.
            read = 1 - read;
        }

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

        var high = (float)time;
        if (patchTime >= 0) gl.Uniform1f(patchTime, high);
        if (patchTimeLow >= 0) gl.Uniform1f(patchTimeLow, (float)(time - high));
        if (patchOne >= 0) gl.Uniform1f(patchOne, 1f);

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
            else Read(live);

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
            // patch coordinates to texel centers, and the flip between a picture
            // indexed downwards and a texture stored upwards. The offset is 0.5
            // on both axes and so is baked into the shader.
            if (patchFeedbackX >= 0)
                gl.Uniform1f(patchFeedbackX, 0.5f * (resolution.Width - 1) / (resolution.Width * aspect));

            if (patchFeedbackY >= 0)
                gl.Uniform1f(patchFeedbackY, 0.5f * (resolution.Height - 1) / resolution.Height);
        }

        // From unit one upward, because nought is the previous frame's and a
        // patch may want both. Every unit is bound whether or not the feedback
        // took its, so a picture's number is its position in the program rather
        // than something that shifts with what else the patch does.
        for (var i = 0; i < pictures.Length && i < shown.Length; i++)
        {
            gl.ActiveTexture(GL_TEXTURE0 + 1 + i);
            gl.BindTexture(GL_TEXTURE_2D, pictures[i]);

            if (patchPictures[i] >= 0) gl.Uniform1i(patchPictures[i], 1 + i);
            if (patchPictureAspects[i] >= 0) gl.Uniform1f(patchPictureAspects[i], shown[i].Aspect);
        }

        // The planes as the frame before left them, on the units above the
        // pictures. The pair alternates with the history, so what is read here is
        // always the side that is not being written.
        for (var i = 0; i < patchPlanes.Length && i < planes[read].Length; i++)
        {
            gl.ActiveTexture(GL_TEXTURE0 + 1 + pictures.Length + i);
            gl.BindTexture(GL_TEXTURE_2D, planes[read][i]);

            if (patchPlanes[i] >= 0) gl.Uniform1i(patchPlanes[i], 1 + pictures.Length + i);
        }

        gl.DrawArrays(GlTriangleStrip, 0, Quad);
    }

    /// <summary>
    /// Lays the block out in the order the program on the card numbers its live
    /// inputs, by name: while the next shader is built, the block is the next
    /// patch's. A handful of keys, so a scan each frame.
    /// </summary>
    private void Read(LiveValues live)
    {
        var keys = live.Keys;

        for (var i = 0; i < played.Length; i++)
        {
            var at = -1;

            if (i < liveInputs.Count)
                for (var k = 0; k < keys.Count && at < 0; k++)
                    if (keys[k] == liveInputs[i])
                        at = k;

            played[i] = (float)live.At(at);
        }
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
    /// space centers it, so there is no offset to carry.
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

                return planeTargets == 0 ? null : Attach(gl, resolution);
            }

            Release(gl);
        }

        return "This GPU will not render to an offscreen buffer.";
    }

    /// <summary>
    /// Says which attachment each of the fragment shader's outputs goes to, for
    /// the dialect whose text cannot. Called between linking and compiling,
    /// because that is the only moment a binding is read.
    /// </summary>
    /// <remarks>
    /// Nothing to do where the shader carried its own locations — ES does, and
    /// saying it twice would be this telling a driver something its own text has
    /// already settled — and nothing to do for a patch with no planes, whose one
    /// output goes to attachment zero wherever the linker puts it.
    /// </remarks>
    private void BindOutputs(int program, int targets)
    {
        if (targets == 0 || dialect is not GlslDialect.Glsl150) return;
        if (bindFragDataLocation is not { } bind) return;

        bind(program, 0, "fragColor");

        for (var target = 0; target < targets; target++)
            bind(program, target + 1, $"outPlane{target}");
    }

    /// <summary>
    /// Hangs the plane targets off both framebuffers and turns the attachments
    /// on. Null on success; a message where the card will not render to a float
    /// surface, which hands the patch to the processor.
    /// </summary>
    /// <remarks>
    /// Full floats first and half floats after. A color tolerates ten bits of
    /// mantissa — ADR-0012 argues that case for the history — but a plane is
    /// usually an accumulator, and an accumulator quantized on every pass drifts
    /// where a color merely bands. Eight-bit is not offered at all: it cannot
    /// hold what a plane is allowed to carry, let alone hold it still.
    /// </remarks>
    private string? Attach(GlInterface gl, PixelSize resolution)
    {
        foreach (var (internalFormat, type) in
                 (ReadOnlySpan<(int, int)>)[(GlRgba32F, GL_FLOAT), (GlRgba16F, GlHalfFloat)])
        {
            var complete = true;

            for (var i = 0; i < 2; i++)
            {
                planes[i] = new int[planeTargets];

                gl.BindFramebuffer(GL_FRAMEBUFFER, framebuffers[i]);

                for (var target = 0; target < planeTargets; target++)
                {
                    planes[i][target] = gl.GenTexture();
                    gl.BindTexture(GL_TEXTURE_2D, planes[i][target]);
                    gl.TexImage2D(
                        GL_TEXTURE_2D, 0, internalFormat,
                        resolution.Width, resolution.Height, 0,
                        GL_RGBA, type, IntPtr.Zero);

                    // Nearest and clamped, unlike the history: a plane is read at
                    // the texel the fragment is, and a filtered read would blend
                    // in the neighbours a plane is defined not to see.
                    gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
                    gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
                    gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
                    gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

                    gl.FramebufferTexture2D(
                        GL_FRAMEBUFFER,
                        GL_COLOR_ATTACHMENT0 + target + 1,
                        GL_TEXTURE_2D,
                        planes[i][target],
                        0);
                }

                Enable();

                complete &= gl.CheckFramebufferStatus(GL_FRAMEBUFFER) == GL_FRAMEBUFFER_COMPLETE;
            }

            if (complete) return null;

            ReleasePlanes(gl);
        }

        // Only the planes go; the history is sound and the message is what sends
        // the frame to the processor, which will free the rest on the way out.
        return "This GPU will not keep a value per pixel between frames.";
    }

    /// <summary>
    /// Names the attachments the shader writes, in order, for the framebuffer
    /// currently bound. A framebuffer draws to its first attachment and no other
    /// until it is told otherwise, so without this the planes would be written
    /// nowhere and read back as the nothing they started as.
    /// </summary>
    private void Enable()
    {
        if (drawBuffers is not { } enable) return;

        Span<int> attachments = stackalloc int[planeTargets + 1];

        for (var i = 0; i < attachments.Length; i++)
            attachments[i] = GL_COLOR_ATTACHMENT0 + i;

        unsafe
        {
            fixed (int* first = attachments)
                enable(attachments.Length, (IntPtr)first);
        }
    }

    private static int? Link(
        GlInterface gl,
        string vertex,
        string fragment,
        out string? error,
        Action<int>? beforeLinking = null)
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

        beforeLinking?.Invoke(program);

        error = gl.LinkProgramAndGetError(program);

        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);

        if (error is null) return program;

        gl.DeleteProgram(program);
        return null;
    }

    /// <summary>Hands the driver a patch's shader to compile and link, without asking how it went.</summary>
    private Building Start(GlInterface gl, ShaderSource shaders)
    {
        var vertex = gl.CreateShader(GL_VERTEX_SHADER);
        gl.ShaderSourceString(vertex, shaders.PatchVertex);
        gl.CompileShader(vertex);

        var fragment = gl.CreateShader(GL_FRAGMENT_SHADER);
        gl.ShaderSourceString(fragment, shaders.PatchFragment);
        gl.CompileShader(fragment);

        var program = gl.CreateProgram();
        gl.AttachShader(program, vertex);
        gl.AttachShader(program, fragment);

        BindOutputs(program, shaders.PlaneTargets);
        gl.LinkProgram(program);

        return new Building(program, vertex, fragment, shaders);
    }

    /// <summary>The linked program, or null with what the driver said. Waits for the driver unless it has finished.</summary>
    private static int? Finish(GlInterface gl, Building built, out string? error)
    {
        error = null;

        if (ShaderParameter(gl, built.Vertex, GlCompileStatus) == 0) error = ShaderLog(gl, built.Vertex);
        else if (ShaderParameter(gl, built.Fragment, GlCompileStatus) == 0) error = ShaderLog(gl, built.Fragment);
        else if (ProgramParameter(gl, built.Program, GlLinkStatus) == 0) error = ProgramLog(gl, built.Program);

        gl.DeleteShader(built.Vertex);
        gl.DeleteShader(built.Fragment);

        if (error is null) return built.Program;

        gl.DeleteProgram(built.Program);
        return null;
    }

    /// <summary>Drops a shader still being built, whose patch has moved on.</summary>
    private void Abandon(GlInterface gl)
    {
        if (building is not { } dropped) return;

        gl.DeleteShader(dropped.Vertex);
        gl.DeleteShader(dropped.Fragment);
        gl.DeleteProgram(dropped.Program);
        building = null;
    }

    private static unsafe int ProgramParameter(GlInterface gl, int program, int name)
    {
        int value;
        gl.GetProgramiv(program, name, &value);
        return value;
    }

    private static unsafe int ShaderParameter(GlInterface gl, int shader, int name)
    {
        int value;
        gl.GetShaderiv(shader, name, &value);
        return value;
    }

    private static unsafe string ShaderLog(GlInterface gl, int shader)
    {
        var log = new byte[8192];

        fixed (byte* at = log)
        {
            gl.GetShaderInfoLog(shader, log.Length, out var length, at);
            return System.Text.Encoding.UTF8.GetString(log, 0, length);
        }
    }

    private static unsafe string ProgramLog(GlInterface gl, int program)
    {
        var log = new byte[8192];

        fixed (byte* at = log)
        {
            gl.GetProgramInfoLog(program, log.Length, out var length, at);
            return System.Text.Encoding.UTF8.GetString(log, 0, length);
        }
    }

    private void Release(GlInterface gl)
    {
        ReleasePlanes(gl);

        for (var i = 0; i < 2; i++)
        {
            if (framebuffers[i] != 0) gl.DeleteFramebuffer(framebuffers[i]);
            if (textures[i] != 0) gl.DeleteTexture(textures[i]);
            framebuffers[i] = 0;
            textures[i] = 0;
        }
    }

    /// <summary>
    /// The planes alone, which one format's attempt hands back before the next is
    /// tried — the framebuffers they were hung off are still good.
    /// </summary>
    private void ReleasePlanes(GlInterface gl)
    {
        for (var i = 0; i < 2; i++)
        {
            foreach (var texture in planes[i])
                if (texture != 0)
                    gl.DeleteTexture(texture);

            planes[i] = [];
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

            // The pictures go here rather than in Release, which is also the
            // resize path: a new size wants new framebuffers and the same
            // photographs, and taking them down there would leave the samplers
            // reading a name that had been handed back the moment somebody
            // dragged the window.
            foreach (var texture in pictures) gl.DeleteTexture(texture);

            Abandon(gl);

            if (patchProgram != 0) gl.DeleteProgram(patchProgram);
            if (blitProgram != 0) gl.DeleteProgram(blitProgram);
            if (vertexArray != 0) gl.DeleteVertexArray(vertexArray);
        }

        Array.Clear(framebuffers);
        Array.Clear(textures);

        // Emptied whether or not there was a context to hand them back to. After
        // a loss the names mean nothing, and an upload that believed it had
        // already done this would bind whatever those numbers now belong to.
        pictures = [];
        shown = [];

        patchProgram = 0;
        blitProgram = 0;
        vertexArray = 0;
        size = default;
        liveSource = string.Empty;
        drawnAt = double.NaN;
        clearPending = true;
    }
}
