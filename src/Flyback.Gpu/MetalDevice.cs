using System.Runtime.InteropServices;

// Names follow the native API they bind.
// ReSharper disable InconsistentNaming

namespace Flyback.Gpu;

/// <summary>
/// A Metal device and its command queue, and the handful of Metal calls the picture
/// makes: compiling a library, building a pipeline, making textures and buffers, and
/// encoding a pass.
/// </summary>
/// <remarks>
/// Every object handed back is owned by the caller, who hands it to <see cref="ObjC.Release"/>,
/// except command buffers and encoders, which are autoreleased and live as long as
/// the pool the frame is drawn in. Metal is safe to call from any thread, one at a time.
/// </remarks>
internal sealed unsafe class MetalDevice : IDisposable
{
    private const string Metal = "/System/Library/Frameworks/Metal.framework/Metal";

    public const nuint R32Float = 55;
    public const nuint Rgba8Unorm = 70;
    public const nuint Bgra8Unorm = 80;
    public const nuint Rgba16Float = 115;
    public const nuint Rgba32Float = 125;

    public const nuint ShaderRead = 1;
    public const nuint RenderTarget = 4;

    private const nuint StorageShared = 0;
    private const nuint StorageManaged = 1;
    private const nuint StoragePrivate = 2;

    private const nuint LoadDontCare = 0;
    private const nuint LoadClear = 2;
    private const nuint StoreStore = 1;
    private const nuint TriangleStrip = 4;
    private const nuint CommandBufferError = 5;

    /// <summary>Exactness over speed in arithmetic: no reassociation, no flushing a NaN to a number.</summary>
    private const nuint MathModeSafe = 0;

    /// <summary>
    /// The builtins (sin, cos, pow) at the GPU's own precision, as OpenGL computes them: the
    /// precise ones made a raymarch a fifth slower, and the picture stays within the bounds
    /// the processor's is held to.
    /// </summary>
    private const nuint FloatingPointFunctionsFast = 0;

    /// <summary>The most <c>setFragmentBytes</c> takes; anything longer goes in a buffer.</summary>
    public const int InlineBytes = 4096;

    private readonly IntPtr device;
    private readonly IntPtr queue;
    private readonly IntPtr options;

    private MetalDevice(IntPtr device, IntPtr queue, IntPtr options)
    {
        this.device = device;
        this.queue = queue;
        this.options = options;

        Name = ObjC.Text(ObjC.Send(device, "name")) ?? "an unnamed GPU";
        Unified = ObjC.Is(device, "hasUnifiedMemory");
    }

    /// <summary>The device's name, e.g. "Apple M2" or "Apple Paravirtual device".</summary>
    public string Name { get; }

    /// <summary>Whether the processor and the GPU share memory, which decides how a texture the processor writes is stored.</summary>
    public bool Unified { get; }

    /// <summary>The system's default device, or null with <paramref name="why"/> saying what stood in the way.</summary>
    public static MetalDevice? Open(out string? why)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst())
        {
            why = "Metal is Apple's, and this is not an Apple platform.";
            return null;
        }

        if (!ObjC.Load(out why)) return null;

        if (!NativeLibrary.TryLoad(Metal, out var metal)
            || !NativeLibrary.TryGetExport(metal, "MTLCreateSystemDefaultDevice", out var create))
        {
            why = "Metal.framework would not load.";
            return null;
        }

        var device = ((delegate* unmanaged<IntPtr>)create)();

        if (device == 0)
        {
            why = "This machine has no Metal device.";
            return null;
        }

        var queue = ObjC.Send(device, "newCommandQueue");

        if (queue == 0)
        {
            ObjC.Release(device);
            why = "The Metal device would not make a command queue.";
            return null;
        }

        why = null;
        return new MetalDevice(device, queue, CompileOptions());
    }

    /// <summary>
    /// Fast arithmetic off, which the shaders need (see <c>MetalEmitter</c>), through the math
    /// mode where the system has one and the older switch where it does not; the builtins at
    /// the GPU's own precision where the system can say so.
    /// </summary>
    private static IntPtr CompileOptions()
    {
        var options = ObjC.New("MTLCompileOptions");

        if (ObjC.RespondsTo(options, "setMathMode:")) ObjC.Call(options, "setMathMode:", MathModeSafe);
        else ObjC.Call(options, "setFastMathEnabled:", false);

        if (ObjC.RespondsTo(options, "setMathFloatingPointFunctions:"))
            ObjC.Call(options, "setMathFloatingPointFunctions:", FloatingPointFunctionsFast);

        return options;
    }

    /// <summary>A library compiled from <paramref name="source"/>, or 0 with <paramref name="error"/> saying why.</summary>
    public IntPtr Library(string source, out string? error)
    {
        var text = ObjC.String(source);
        IntPtr failure = 0;

        var library = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr*, IntPtr>)ObjC.MsgSend)(
            device, ObjC.Sel("newLibraryWithSource:options:error:"), text, options, &failure);

        ObjC.Release(text);

        error = library == 0 ? ObjC.Describe(failure, "The library would not compile.") : null;
        return library;
    }

    /// <summary>
    /// A pipeline drawing <paramref name="fragment"/> into one attachment per format, or 0
    /// with <paramref name="error"/> saying why.
    /// </summary>
    public IntPtr Pipeline(IntPtr library, string vertex, string fragment, ReadOnlySpan<nuint> formats, out string? error)
    {
        var vertexName = ObjC.String(vertex);
        var fragmentName = ObjC.String(fragment);
        var vertexFunction = ObjC.Send(library, "newFunctionWithName:", vertexName);
        var fragmentFunction = ObjC.Send(library, "newFunctionWithName:", fragmentName);

        ObjC.Release(vertexName);
        ObjC.Release(fragmentName);

        if (vertexFunction == 0 || fragmentFunction == 0)
        {
            ObjC.Release(vertexFunction);
            ObjC.Release(fragmentFunction);
            error = $"The library has no {vertex} or no {fragment}.";
            return 0;
        }

        var descriptor = ObjC.New("MTLRenderPipelineDescriptor");
        ObjC.Call(descriptor, "setVertexFunction:", vertexFunction);
        ObjC.Call(descriptor, "setFragmentFunction:", fragmentFunction);

        var attachments = ObjC.Send(descriptor, "colorAttachments");

        for (var i = 0; i < formats.Length; i++)
            ObjC.Call(ObjC.Send(attachments, "objectAtIndexedSubscript:", (nuint)i), "setPixelFormat:", formats[i]);

        IntPtr failure = 0;

        var pipeline = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr*, IntPtr>)ObjC.MsgSend)(
            device, ObjC.Sel("newRenderPipelineStateWithDescriptor:error:"), descriptor, &failure);

        error = pipeline == 0 ? ObjC.Describe(failure, "The pipeline would not build.") : null;

        ObjC.Release(descriptor);
        ObjC.Release(vertexFunction);
        ObjC.Release(fragmentFunction);

        return pipeline;
    }

    /// <summary>
    /// A two-dimensional texture. One the processor writes is kept where it can reach it;
    /// one only the GPU touches is the GPU's alone.
    /// </summary>
    public IntPtr Texture(nuint format, int width, int height, nuint usage, bool written)
    {
        var descriptor = ((delegate* unmanaged<IntPtr, IntPtr, nuint, nuint, nuint, byte, IntPtr>)ObjC.MsgSend)(
            ObjC.Class("MTLTextureDescriptor"),
            ObjC.Sel("texture2DDescriptorWithPixelFormat:width:height:mipmapped:"),
            format,
            (nuint)width,
            (nuint)height,
            0);

        ObjC.Call(descriptor, "setUsage:", usage);
        ObjC.Call(descriptor, "setStorageMode:", written ? (Unified ? StorageShared : StorageManaged) : StoragePrivate);

        return ObjC.Send(device, "newTextureWithDescriptor:", descriptor);
    }

    /// <summary>Writes rows from <paramref name="bytes"/> into a texture the processor writes.</summary>
    public static void Upload(IntPtr texture, int width, int height, void* bytes, int bytesPerRow)
    {
        var region = new Region(0, 0, 0, (nuint)width, (nuint)height, 1);

        ((delegate* unmanaged<IntPtr, IntPtr, Region, nuint, void*, nuint, void>)ObjC.MsgSend)(
            texture, ObjC.Sel("replaceRegion:mipmapLevel:withBytes:bytesPerRow:"), region, 0, bytes, (nuint)bytesPerRow);
    }

    /// <summary>A buffer both the processor and the GPU reach, of <paramref name="length"/> bytes.</summary>
    public IntPtr Buffer(int length) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nuint, nuint, IntPtr>)ObjC.MsgSend)(
            device, ObjC.Sel("newBufferWithLength:options:"), (nuint)Math.Max(length, 16), StorageShared << 4);

    /// <summary>Where a buffer's bytes are, for the processor.</summary>
    public static IntPtr Contents(IntPtr buffer) => ObjC.Send(buffer, "contents");

    /// <summary>A command buffer, autoreleased.</summary>
    public IntPtr Commands() => ObjC.Send(queue, "commandBuffer");

    /// <summary>
    /// Starts a pass drawing into <paramref name="targets"/>, cleared to black first or
    /// left to be drawn over whole. The picture's alpha is <paramref name="alpha"/>; the
    /// rest start transparent, holding nothing.
    /// </summary>
    public static IntPtr Pass(IntPtr commands, ReadOnlySpan<IntPtr> targets, bool clear, double alpha)
    {
        var descriptor = ObjC.Send(ObjC.Class("MTLRenderPassDescriptor"), "renderPassDescriptor");
        var attachments = ObjC.Send(descriptor, "colorAttachments");

        for (var i = 0; i < targets.Length; i++)
        {
            var attachment = ObjC.Send(attachments, "objectAtIndexedSubscript:", (nuint)i);

            ObjC.Call(attachment, "setTexture:", targets[i]);
            ObjC.Call(attachment, "setLoadAction:", clear ? LoadClear : LoadDontCare);
            ObjC.Call(attachment, "setStoreAction:", StoreStore);

            if (clear)
                ((delegate* unmanaged<IntPtr, IntPtr, ClearColor, void>)ObjC.MsgSend)(
                    attachment, ObjC.Sel("setClearColor:"), new ClearColor(0, 0, 0, i == 0 ? alpha : 0));
        }

        return ObjC.Send(commands, "renderCommandEncoderWithDescriptor:", descriptor);
    }

    public static void Use(IntPtr encoder, IntPtr pipeline) => ObjC.Call(encoder, "setRenderPipelineState:", pipeline);

    public static void FragmentBytes(IntPtr encoder, void* bytes, int length, int index) =>
        ((delegate* unmanaged<IntPtr, IntPtr, void*, nuint, nuint, void>)ObjC.MsgSend)(
            encoder, ObjC.Sel("setFragmentBytes:length:atIndex:"), bytes, (nuint)length, (nuint)index);

    public static void VertexBytes(IntPtr encoder, void* bytes, int length, int index) =>
        ((delegate* unmanaged<IntPtr, IntPtr, void*, nuint, nuint, void>)ObjC.MsgSend)(
            encoder, ObjC.Sel("setVertexBytes:length:atIndex:"), bytes, (nuint)length, (nuint)index);

    public static void FragmentBuffer(IntPtr encoder, IntPtr buffer, int index) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, nuint, nuint, void>)ObjC.MsgSend)(
            encoder, ObjC.Sel("setFragmentBuffer:offset:atIndex:"), buffer, 0, (nuint)index);

    public static void FragmentTexture(IntPtr encoder, IntPtr texture, int index) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, nuint, void>)ObjC.MsgSend)(
            encoder, ObjC.Sel("setFragmentTexture:atIndex:"), texture, (nuint)index);

    /// <summary>The unit square as a four-vertex strip, its corners made in the vertex function.</summary>
    public static void DrawQuad(IntPtr encoder) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nuint, nuint, nuint, void>)ObjC.MsgSend)(
            encoder, ObjC.Sel("drawPrimitives:vertexStart:vertexCount:"), TriangleStrip, 0, 4);

    public static void End(IntPtr encoder) => ObjC.Call(encoder, "endEncoding");

    /// <summary>Copies a whole texture into a buffer, row after row.</summary>
    public static void Copy(IntPtr commands, IntPtr texture, int width, int height, int bytesPerPixel, IntPtr buffer)
    {
        var blit = ObjC.Send(commands, "blitCommandEncoder");
        var row = (nuint)(width * bytesPerPixel);

        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, nuint, nuint, Origin, Size, IntPtr, nuint, nuint, nuint, void>)ObjC.MsgSend)(
            blit,
            ObjC.Sel("copyFromTexture:sourceSlice:sourceLevel:sourceOrigin:sourceSize:toBuffer:destinationOffset:destinationBytesPerRow:destinationBytesPerImage:"),
            texture,
            0,
            0,
            new Origin(0, 0, 0),
            new Size((nuint)width, (nuint)height, 1),
            buffer,
            0,
            row,
            row * (nuint)height);

        End(blit);
    }

    /// <summary>Sends the work and waits for the GPU to finish it. Null on success.</summary>
    public static string? Finish(IntPtr commands)
    {
        ObjC.Call(commands, "commit");

        return Wait(commands);
    }

    /// <summary>
    /// Sends the work without waiting for it, keeping the command buffer past the frame's
    /// pool for <see cref="Wait"/>; the caller releases it after.
    /// </summary>
    public static IntPtr Send(IntPtr commands)
    {
        ObjC.Send(commands, "retain");
        ObjC.Call(commands, "commit");

        return commands;
    }

    /// <summary>Waits for work already sent. Null on success.</summary>
    public static string? Wait(IntPtr commands)
    {
        ObjC.Call(commands, "waitUntilCompleted");

        return (nuint)ObjC.Send(commands, "status") == CommandBufferError
            ? ObjC.Describe(ObjC.Send(commands, "error"), "The GPU failed the frame.")
            : null;
    }

    public void Dispose()
    {
        ObjC.Release(options);
        ObjC.Release(queue);
        ObjC.Release(device);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Origin(nuint X, nuint Y, nuint Z);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Size(nuint Width, nuint Height, nuint Depth);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Region(nuint X, nuint Y, nuint Z, nuint Width, nuint Height, nuint Depth);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct ClearColor(double Red, double Green, double Blue, double Alpha);
}
