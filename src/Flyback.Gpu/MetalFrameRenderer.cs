using System.Runtime.InteropServices;
using Flyback.Core.Compile;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;

namespace Flyback.Gpu;

/// <summary>
/// A patch's frames drawn through Metal: <see cref="GpuFrameRenderer"/>'s work on
/// Apple's own API, from the library <see cref="MetalEmitter"/> writes.
/// </summary>
/// <remarks>
/// The history is a pair of half-float textures and the planes a pair of float ones,
/// ping-ponged as on OpenGL, and stored bottom row first as OpenGL stores them. A frame
/// is read back after the GPU blits it to eight-bit BGRA top row first, so the processor
/// only copies it. A library compiles in well under a second where OpenGL ES took minutes, and on any
/// thread, so nothing here waits on another thread's link. Methods return an error
/// string rather than throw, as the OpenGL renderer's do.
/// </remarks>
/// <param name="overlapped">
/// Whether a frame hands back the one drawn before it while the GPU draws it: a frame
/// late, as the preview can afford, for the GPU and the window to work at once. A render
/// and the tests want the frame they asked for.
/// </param>
internal sealed unsafe class MetalFrameRenderer(MetalDevice device, bool overlapped = false) : IDisposable
{
    private const int ResolvedBytes = 4;
    private const int Remembered = 8;

    private IntPtr pipeline;
    private MetalShaderSource? source;
    private string liveSource = string.Empty;
    private string refusedSource = string.Empty;

    /// <summary>Pipelines that have been drawn with, oldest first, for an undo or a redo to come back to.</summary>
    private readonly List<(string Source, IntPtr Pipeline, nuint Planes)> kept = [];

    private float[] constants = [];
    private IntPtr constantBuffer;
    private IReadOnlyList<string> liveInputs = [];
    private float[] played = [];

    private IntPtr[] pictures = [];
    private LoadedImage[] shown = [];

    private IntPtr[] tables = [];
    private LoadedSample[] held = [];
    private bool[] charts = [];

    private readonly IntPtr[] history = [0, 0];
    private readonly IntPtr[][] planes = [[], []];

    /// <summary>The format the planes are kept in: full floats, or half where the GPU will not attach that many full ones.</summary>
    private nuint planeFormat = MetalDevice.Rgba32Float;

    private int planeTargets;
    private SurfaceSize size;
    private int read;
    private bool clearPending = true;
    private double drawnAt = double.NaN;

    /// <summary>Where a frame is read back to, two so one can be read while the GPU writes the other.</summary>
    private readonly IntPtr[] readbacks = [0, 0];
    private int readbackLength;
    private int writing;

    /// <summary>The frame sent and not yet read back, with its readback and its size.</summary>
    private (IntPtr Commands, int Readback, SurfaceSize Size)? sent;

    /// <summary>The patch last set, whose library is the one on the card: a frame of the same patch emits nothing.</summary>
    private CompiledPatch? set;

    /// <summary>
    /// The blit, drawing the history into <see cref="resolved"/> as the bytes a frame is
    /// read back as: eight bits, BGRA, top row first, converted by the GPU.
    /// </summary>
    private IntPtr resolve;
    private IntPtr resolved;

    /// <summary>The blit's pipeline with the smoothing pass in place of a plain copy.</summary>
    private IntPtr smoothed;

    /// <summary>
    /// How the frame's edges are smoothed as it is resolved. Best is drawn as Fast
    /// here: four samples a pixel is a render's, not a window's.
    /// </summary>
    public Smoothing Smoothing { get; set; }

    /// <summary>
    /// Points the renderer at a program, compiling a library for it if the text has
    /// changed. Null on success, including when there was nothing to do.
    /// </summary>
    public string? SetPatch(CompiledPatch patch)
    {
        // The preview hands the same patch every frame until it is edited.
        if (ReferenceEquals(patch, set)) return null;

        set = patch;

        var shaders = MetalEmitter.Emit(patch);

        if (shaders.Library == refusedSource) return null;

        if (shaders.Library != liveSource)
        {
            var at = kept.FindIndex(k => k.Source == shaders.Library);

            if (at >= 0)
            {
                var (_, recalled, format) = kept[at];
                kept.RemoveAt(at);
                Install(recalled, format, shaders);
            }
            else if (Build(shaders, out var built, out var format) is { } error)
            {
                refusedSource = shaders.Library;
                return $"The patch would not compile for Metal. {error}";
            }
            else
            {
                Install(built, format, shaders);
            }
        }

        Adopt(patch);
        return null;
    }

    /// <summary>
    /// Compiles the library and its pipeline, the planes in full floats and then in half
    /// where the GPU's tile memory will not hold that many full ones.
    /// </summary>
    private string? Build(MetalShaderSource shaders, out IntPtr built, out nuint format)
    {
        built = 0;
        format = MetalDevice.Rgba32Float;

        var library = device.Library(shaders.Library, out var error);
        if (library == 0) return error;

        Span<nuint> formats = stackalloc nuint[1 + shaders.PlaneTargets];
        formats[0] = MetalDevice.Rgba16Float;

        try
        {
            foreach (var candidate in (ReadOnlySpan<nuint>)[MetalDevice.Rgba32Float, MetalDevice.Rgba16Float])
            {
                formats[1..].Fill(candidate);

                built = device.Pipeline(library, MetalEmitter.PatchVertex, MetalEmitter.PatchFragment, formats, out error);
                format = candidate;

                if (built != 0 || shaders.PlaneTargets == 0) break;
            }

            return built == 0 ? error : null;
        }
        finally
        {
            ObjC.Release(library);
        }
    }

    private void Install(IntPtr built, nuint format, MetalShaderSource shaders)
    {
        if (pipeline != 0)
        {
            kept.Add((liveSource, pipeline, planeFormat));

            if (kept.Count > Remembered)
            {
                ObjC.Release(kept[0].Pipeline);
                kept.RemoveAt(0);
            }
        }

        pipeline = built;
        source = shaders;
        liveSource = shaders.Library;

        // The planes are the pipeline's attachments, so a patch that gained or lost a
        // loop, or keeps it in another format, needs them made again.
        if (shaders.PlaneTargets != planeTargets || format != planeFormat)
        {
            planeTargets = shaders.PlaneTargets;
            planeFormat = format;
            size = default;
        }

        played = new float[shaders.LiveCount];
    }

    /// <summary>
    /// What a patch hands the program beside its text: the values behind its constants,
    /// its pictures and tables, and the names of its live inputs.
    /// </summary>
    private void Adopt(CompiledPatch patch)
    {
        var values = GlslEmitter.Constants(patch);

        if (values.Length * sizeof(float) > MetalDevice.InlineBytes && !values.AsSpan().SequenceEqual(constants))
        {
            if (constantBuffer == 0 || values.Length != constants.Length)
            {
                ObjC.Release(constantBuffer);
                constantBuffer = device.Buffer(values.Length * sizeof(float));
            }

            values.CopyTo(new Span<float>((void*)MetalDevice.Contents(constantBuffer), values.Length));
        }

        constants = values;
        liveInputs = patch.LiveInputs;

        UploadPictures(patch.Pictures);
        UploadTables(patch);
    }

    /// <summary>The patch's pictures as eight-bit textures, uploaded again only when they are other pictures.</summary>
    private void UploadPictures(IReadOnlyList<LoadedImage> wanted)
    {
        if (pictures.Length == wanted.Count && Same(shown, wanted)) return;

        foreach (var texture in pictures) ObjC.Release(texture);

        pictures = new IntPtr[wanted.Count];
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

            pictures[i] = device.Texture(MetalDevice.Rgba8Unorm, picture.Width, picture.Height, MetalDevice.ShaderRead, written: true);

            fixed (byte* first = bytes)
                MetalDevice.Upload(pictures[i], picture.Width, picture.Height, first, picture.Width * 4);
        }
    }

    /// <summary>The patch's clips and charts as float textures in rows of <see cref="GlslEmitter.TableRow"/>.</summary>
    private void UploadTables(CompiledPatch patch)
    {
        var wanted = patch.Tables;

        charts = [.. wanted.Select(table => patch.Taps.Any(tap => ReferenceEquals(tap.Trace, table)))];

        if (held.Length == wanted.Count && Same(held, wanted)) return;

        foreach (var texture in tables) ObjC.Release(texture);

        tables = new IntPtr[wanted.Count];
        held = [.. wanted];

        for (var i = 0; i < wanted.Count; i++)
        {
            var (width, rows) = Shape(held[i]);

            tables[i] = device.Texture(MetalDevice.R32Float, width, rows, MetalDevice.ShaderRead, written: true);
            UploadTable(i);
        }
    }

    private static (int Width, int Rows) Shape(LoadedSample table)
    {
        var length = table.Samples.Length;

        return (Math.Clamp(length, 1, GlslEmitter.TableRow), Math.Max(1, (length + GlslEmitter.TableRow - 1) / GlslEmitter.TableRow));
    }

    private void UploadTable(int index)
    {
        var table = held[index];
        var (width, rows) = Shape(table);

        // Padded out to whole rows, except where it already fills them, as a chart's buffer does.
        var floats = table.Samples.Length == width * rows ? table.Samples : new float[width * rows];
        if (!ReferenceEquals(floats, table.Samples)) table.Samples.CopyTo(floats, 0);

        fixed (float* first = floats)
            MetalDevice.Upload(tables[index], width, rows, first, width * sizeof(float));
    }

    private static bool Same<T>(T[] have, IReadOnlyList<T> want) where T : class
    {
        for (var i = 0; i < have.Length; i++)
            if (!ReferenceEquals(have[i], want[i]))
                return false;

        return true;
    }

    private static byte Byte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    /// <summary>Clears the feedback history, so the next frame starts from black.</summary>
    public void Rewind() => clearPending = true;

    /// <summary>
    /// Draws one frame at <paramref name="resolution"/> and reads it into
    /// <paramref name="destination"/>, top row first and BGRA, opaque as the processor
    /// draws it. Null on success.
    /// </summary>
    public string? Frame(SurfaceSize resolution, double time, LiveValues? live, Span<byte> destination, int stride)
    {
        if (resolution.Width <= 0 || resolution.Height <= 0) return "A frame needs both dimensions.";
        if (pipeline == 0 || source is null) return "There is no patch to draw.";

        var pool = ObjC.Push();

        try
        {
            if (Resize(resolution) is { } failure) return failure;
            if (Resolver() is { } missing) return missing;

            var commands = device.Commands();

            Draw(commands, resolution, time, live);
            Resolve(commands, resolution);

            var row = resolution.Width * ResolvedBytes;
            var bytes = row * resolution.Height;

            if (readbackLength != bytes)
            {
                Settle();

                for (var k = 0; k < 2; k++)
                {
                    ObjC.Release(readbacks[k]);
                    readbacks[k] = device.Buffer(bytes);
                }

                readbackLength = bytes;
            }

            MetalDevice.Copy(commands, resolved, resolution.Width, resolution.Height, ResolvedBytes, readbacks[writing]);

            var now = (Commands: MetalDevice.Send(commands), Readback: writing, Size: resolution);
            writing = 1 - writing;

            // Overlapped, the frame before where there is one of this size, and this one is
            // kept for the next call; otherwise this one, waited for. The first overlapped
            // frame is shown twice, which is what starts the overlap.
            var late = overlapped && sent is { } before && before.Size == resolution;

            if (!late) Settle();

            var shown = late ? sent!.Value : now;
            var error = MetalDevice.Wait(shown.Commands);
            var frame = new ReadOnlySpan<byte>((void*)MetalDevice.Contents(readbacks[shown.Readback]), bytes);

            if (error is null)
            {
                if (stride == row) frame.CopyTo(destination);
                else
                    for (var y = 0; y < resolution.Height; y++)
                        frame.Slice(y * row, row).CopyTo(destination.Slice(y * stride, row));
            }

            if (late) ObjC.Release(shown.Commands);

            if (overlapped)
            {
                sent = now;
            }
            else
            {
                ObjC.Release(now.Commands);
                sent = null;
            }

            return error;
        }
        finally
        {
            ObjC.Pop(pool);
        }
    }

    /// <summary>Waits out a frame sent and not read back, and lets it go.</summary>
    private void Settle()
    {
        if (sent is not { } waiting) return;

        MetalDevice.Wait(waiting.Commands);
        ObjC.Release(waiting.Commands);
        sent = null;
    }

    /// <summary>The blit pipeline, built once. Null when it is there.</summary>
    private string? Resolver()
    {
        if (resolve != 0) return null;

        var library = device.Library(MetalEmitter.Emit(CompiledPatch.Black).Library, out var error);
        if (library == 0) return $"The blit would not compile for Metal. {error}";

        resolve = device.Pipeline(library, MetalEmitter.BlitVertex, MetalEmitter.BlitFragment, [MetalDevice.Bgra8Unorm], out error);
        smoothed = resolve == 0 ? 0 : device.Pipeline(library, MetalEmitter.BlitVertex, MetalEmitter.SmoothingFragment, [MetalDevice.Bgra8Unorm], out error);
        ObjC.Release(library);

        if (resolve != 0 && smoothed == 0)
        {
            ObjC.Release(resolve);
            resolve = 0;
        }

        return resolve == 0 ? $"The blit would not build for Metal. {error}" : null;
    }

    /// <summary>
    /// Draws the frame just drawn into <see cref="resolved"/>, unscaled: the blit samples
    /// texel centers, so each byte is the GPU's rounding of its half float.
    /// </summary>
    private void Resolve(IntPtr commands, SurfaceSize resolution)
    {
        var encoder = MetalDevice.Pass(commands, [resolved], clear: false, alpha: 1);
        var scale = stackalloc float[] { 1f, 1f };
        var texel = stackalloc float[] { 1f / resolution.Width, 1f / resolution.Height };

        MetalDevice.Use(encoder, Smoothing == Smoothing.Off ? resolve : smoothed);
        MetalDevice.VertexBytes(encoder, scale, 2 * sizeof(float), MetalEmitter.BlitBuffer);
        if (Smoothing != Smoothing.Off) MetalDevice.FragmentBytes(encoder, texel, 2 * sizeof(float), MetalEmitter.SmoothingBuffer);
        MetalDevice.FragmentTexture(encoder, history[read], 0);
        MetalDevice.DrawQuad(encoder);
        MetalDevice.End(encoder);
    }

    /// <summary>Makes the history pair, and the planes' beside it, for <paramref name="resolution"/>.</summary>
    private string? Resize(SurfaceSize resolution)
    {
        if (size == resolution && history[0] != 0) return null;

        ReleaseTargets();
        size = resolution;
        clearPending = true;

        for (var i = 0; i < 2; i++)
        {
            history[i] = device.Texture(
                MetalDevice.Rgba16Float, resolution.Width, resolution.Height, MetalDevice.RenderTarget | MetalDevice.ShaderRead, written: false);

            planes[i] = new IntPtr[planeTargets];

            for (var target = 0; target < planeTargets; target++)
                planes[i][target] = device.Texture(
                    planeFormat, resolution.Width, resolution.Height, MetalDevice.RenderTarget | MetalDevice.ShaderRead, written: false);
        }

        resolved = device.Texture(MetalDevice.Bgra8Unorm, resolution.Width, resolution.Height, MetalDevice.RenderTarget, written: false);

        return history[0] == 0 || history[1] == 0 || resolved == 0 || planes.Any(pair => pair.Contains(0))
            ? "This GPU will not make the textures a frame is drawn into."
            : null;
    }

    private void Draw(IntPtr commands, SurfaceSize resolution, double time, LiveValues? live)
    {
        if (clearPending)
        {
            for (var i = 0; i < 2; i++)
                MetalDevice.End(MetalDevice.Pass(commands, Targets(i), clear: true, alpha: planeTargets == 0 ? 1 : 0));

            clearPending = false;
        }

        var shaders = source!;
        var encoder = MetalDevice.Pass(commands, Targets(1 - read), clear: false, alpha: 1);

        MetalDevice.Use(encoder, pipeline);

        var aspect = resolution.Height == 0 ? 1f : (float)resolution.Width / resolution.Height;
        var high = (float)time;

        var frame = stackalloc float[MetalEmitter.FrameFloats];
        frame[0] = high;
        frame[1] = (float)(time - high);
        frame[2] = 1f;
        frame[3] = aspect;
        frame[4] = (float)SynthRenderer.AgeOf(time, drawnAt);
        frame[5] = 0.5f * (resolution.Width - 1) / (resolution.Width * aspect);
        frame[6] = 0.5f * (resolution.Height - 1) / resolution.Height;

        MetalDevice.FragmentBytes(encoder, frame, MetalEmitter.FrameFloats * sizeof(float), MetalEmitter.FrameBuffer);

        if (shaders.ConstantCount > 0)
        {
            if (constants.Length * sizeof(float) > MetalDevice.InlineBytes)
                MetalDevice.FragmentBuffer(encoder, constantBuffer, MetalEmitter.ConstantsBuffer);
            else
                Bytes(encoder, constants, shaders.ConstantCount, MetalEmitter.ConstantsBuffer);
        }

        // Copied out of the block before it is bound, so the whole frame is drawn with one reading of the keys.
        if (shaders.LiveCount > 0)
        {
            if (live is null) Array.Clear(played);
            else Read(live);

            Bytes(encoder, played, shaders.LiveCount, MetalEmitter.LiveBuffer);
        }

        if (shaders.PictureCount > 0)
        {
            var aspects = new float[shaders.PictureCount];
            for (var i = 0; i < shown.Length && i < aspects.Length; i++) aspects[i] = shown[i].Aspect;

            Bytes(encoder, aspects, aspects.Length, MetalEmitter.PicturesBuffer);
        }

        if (shaders.TableCount > 0)
        {
            var shapes = new float[shaders.TableCount * 2];

            for (var i = 0; i < held.Length && i < shaders.TableCount; i++)
            {
                if (charts[i]) UploadTable(i);

                shapes[2 * i] = held[i].Samples.Length;
                shapes[2 * i + 1] = held[i].SampleRate;
            }

            Bytes(encoder, shapes, shapes.Length, MetalEmitter.TablesBuffer);
        }

        if (shaders.UsesFeedback) MetalDevice.FragmentTexture(encoder, history[read], 0);

        for (var i = 0; i < pictures.Length && i < shaders.PictureCount; i++)
            MetalDevice.FragmentTexture(encoder, pictures[i], 1 + i);

        for (var i = 0; i < planeTargets; i++)
            MetalDevice.FragmentTexture(encoder, planes[read][i], 1 + shaders.PictureCount + i);

        for (var i = 0; i < tables.Length && i < shaders.TableCount; i++)
            MetalDevice.FragmentTexture(encoder, tables[i], 1 + shaders.PictureCount + planeTargets + i);

        MetalDevice.DrawQuad(encoder);
        MetalDevice.End(encoder);

        drawnAt = time;

        // The frame just drawn becomes the one the next frame reads back.
        read = 1 - read;
    }

    /// <summary>The history texture and the planes one side of the pair draws into.</summary>
    private IntPtr[] Targets(int side) => [history[side], .. planes[side]];

    /// <summary>A short array bound by value; a binding of nothing is a binding Metal refuses, so it is never empty.</summary>
    private static void Bytes(IntPtr encoder, float[] values, int count, int index)
    {
        Span<float> padded = stackalloc float[Math.Max(count, 1)];
        values.AsSpan(0, Math.Min(count, values.Length)).CopyTo(padded);

        fixed (float* first = padded)
            MetalDevice.FragmentBytes(encoder, first, padded.Length * sizeof(float), index);
    }

    /// <summary>Lays the live block out in the order the program numbers its live inputs, by name.</summary>
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

    private void ReleaseTargets()
    {
        ObjC.Release(resolved);
        resolved = 0;

        for (var i = 0; i < 2; i++)
        {
            ObjC.Release(history[i]);
            history[i] = 0;

            foreach (var plane in planes[i]) ObjC.Release(plane);
            planes[i] = [];
        }
    }

    public void Dispose()
    {
        ReleaseTargets();

        foreach (var texture in pictures) ObjC.Release(texture);
        foreach (var texture in tables) ObjC.Release(texture);
        foreach (var (_, old, _) in kept) ObjC.Release(old);

        ObjC.Release(pipeline);
        ObjC.Release(resolve);
        ObjC.Release(smoothed);
        Settle();

        ObjC.Release(constantBuffer);
        foreach (var buffer in readbacks) ObjC.Release(buffer);

        pipeline = 0;
        resolve = 0;
        smoothed = 0;
        constantBuffer = 0;
        readbacks[0] = readbacks[1] = 0;
        pictures = [];
        tables = [];
        kept.Clear();
    }
}
