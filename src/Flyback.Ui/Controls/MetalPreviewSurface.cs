using Avalonia;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Flyback.Gpu;

namespace Flyback.Ui.Controls;

/// <summary>
/// The picture drawn through Metal (ADR-0214), on a thread of the pool, and shown as
/// the processor's picture is shown: a bitmap, letterboxed.
/// </summary>
/// <remarks>
/// Nothing here runs on the UI thread but the copy into the bitmap: a patch's library
/// compiles in the frame that first draws it, and a slow compile holds the last frame
/// rather than the window. Reading the frame back costs a copy, which unified memory
/// makes a small one, and a recording takes the frame from there.
/// </remarks>
public sealed class MetalPreviewSurface : PreviewSurface, IGpuPreview, ICapturedPreview
{
    private readonly Picture picture;
    private bool failed;

    public MetalPreviewSurface() : this(new Picture())
    {
    }

    private MetalPreviewSurface(Picture picture) : base(picture, picture.Rewind, "Metal frame") => this.picture = picture;

    public event Action<string>? Failed;

    /// <summary>Metal once a device has opened, and null before.</summary>
    public string? Api => picture.Opened ? GraphicsApi.Metal : null;

    public bool ProcessorStandsIn => true;

    /// <summary>Who wants the frames, handed each one on the thread that drew it.</summary>
    IFrameSink? ICapturedPreview.Capture { get => picture.Capture; set => picture.Capture = value; }

    /// <summary>Every frame is in memory already, so a recording is never refused here.</summary>
    string? ICapturedPreview.CaptureUnavailable => null;

    /// <summary>Said once, and then this surface stops drawing: the next frame would fail the same way.</summary>
    private protected override void OnFrameFailed(Exception ex)
    {
        if (failed) return;

        failed = true;
        Stop();
        Failed?.Invoke(ex.Message);
    }

    private protected override void Retire() => picture.Dispose();

    /// <summary>Handed to Metal, which smooths the frame as it resolves it.</summary>
    public override Smoothing Smoothing
    {
        get => picture.Smoothing;
        set => picture.Smoothing = value;
    }

    private protected override void Smooth(Smoothing smoothing, byte[] buffer, PixelSize size, int stride)
    {
    }

    /// <summary>No rest: the frame's cost is the graphics card's, and the processor waited it out idle.</summary>
    private protected override double Rest => 0;

    /// <summary>
    /// The Metal renderer, opened by the first frame and so on the pool rather than the
    /// UI thread, and rewound there before the next frame is drawn.
    /// </summary>
    private sealed class Picture : IFrameRenderer, IDisposable
    {
        private MetalHeadlessRenderer? renderer;
        private volatile bool rewind;
        private bool retired;
        private byte[] captured = [];

        public IFrameSink? Capture
        {
            get => Volatile.Read(ref field);
            set => Volatile.Write(ref field, value);
        }

        /// <summary>Set from the UI thread, read by the next frame.</summary>
        public Smoothing Smoothing { get; set; }

        /// <summary>Whether a device has opened, which stays said once the surface is retired, for a report written after.</summary>
        public bool Opened => Volatile.Read(ref opened);

        private bool opened;

        /// <summary>Asked from the UI thread; done by the next frame, on the thread that draws it.</summary>
        public void Rewind() => rewind = true;

        public void Render(
            CompiledPatch patch,
            double time,
            int width,
            int height,
            Span<byte> destination,
            int stride,
            LiveValues? live = null)
        {
            ObjectDisposedException.ThrowIf(retired, this);

            if (renderer is null)
            {
                // A frame late, so the GPU draws the next while the window shows this one.
                var device = MetalHeadlessRenderer.Open(out var why, overlapped: true)
                    ?? throw new InvalidOperationException(why ?? "Metal would not open.");

                renderer = device;
                Volatile.Write(ref opened, true);
            }

            if (rewind)
            {
                rewind = false;
                renderer.Reset();
            }

            renderer.Smoothing = Smoothing;
            renderer.Render(patch, time, width, height, destination, stride, live);

            if (Capture is { } sink) Hand(sink, destination, width, height, stride);
        }

        /// <summary>
        /// The frame as a recording takes it from OpenGL: RGBA, tightly packed, bottom
        /// row first.
        /// </summary>
        private void Hand(IFrameSink sink, ReadOnlySpan<byte> bgra, int width, int height, int stride)
        {
            var row = width * 4;

            if (captured.Length != row * height) captured = new byte[row * height];

            for (var y = 0; y < height; y++)
            {
                var from = bgra.Slice(y * stride, row);
                var to = captured.AsSpan((height - 1 - y) * row, row);

                for (var x = 0; x < row; x += 4)
                {
                    to[x + 0] = from[x + 2];
                    to[x + 1] = from[x + 1];
                    to[x + 2] = from[x + 0];
                    to[x + 3] = from[x + 3];
                }
            }

            sink.Accept(captured, width, height);
        }

        /// <summary>Called on the UI thread with no frame in flight.</summary>
        public void Dispose()
        {
            retired = true;
            renderer?.Dispose();
            renderer = null;
        }
    }
}
