using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Flyback.Core.Compile;

namespace Flyback.App.Controls;

/// <summary>Which renderer is drawing the preview.</summary>
public enum PreviewBackend
{
    /// <summary>The interpreter, over rows, on the processor.</summary>
    Cpu,

    /// <summary>The patch compiled to a fragment shader.</summary>
    Gpu,
}

/// <summary>
/// Holds whichever preview renderer is currently running and forwards the shell's
/// dealings with it. The window asks for a preview; this decides what one is.
/// </summary>
/// <remarks>
/// <para>
/// It starts optimistic. If the GPU turns out not to be usable — no context, a
/// shader that will not build, a context that keeps being lost — the CPU
/// renderer takes over and the reason is said once. There is no way back from
/// that within a session: whatever it was will not have fixed itself by the next
/// frame, and a preview that flickers between backends is worse than either.
/// </para>
/// <para>
/// A person may still switch to the CPU by hand, and back again while the GPU is
/// still on offer. That is the escape hatch for the one thing the shader backend
/// is genuinely worse at — see ADR-0035 on what float32 does to an oscillator
/// after an hour.
/// </para>
/// <para>
/// Everything the shell sets on a preview is carried across a swap, so switching
/// renderers hands the new one the state the old one had. The picture is the only
/// thing that does not survive, and a frame later there is a new one.
/// </para>
/// </remarks>
public sealed class PreviewHost : Decorator, IPreviewSurface
{
    /// <summary>
    /// The renderer on screen. Empty for exactly as long as the constructor
    /// takes to put one there, which is why <see cref="Switch"/> promises to
    /// fill it rather than this being left nullable for every reader after.
    /// </summary>
    private IPreviewSurface active;

    public PreviewHost() => Switch(PreviewBackend.Gpu);

    /// <summary>Which renderer is drawing now.</summary>
    public PreviewBackend Backend { get; private set; }

    /// <summary>False once the GPU has refused, after which it is not offered again.</summary>
    public bool GpuAvailable { get; private set; } = true;

    /// <summary>
    /// Raised when the renderer changes, with something to put on the status bar
    /// when the change was not asked for.
    /// </summary>
    public event Action<string>? BackendChanged;

    /// <summary>What to call the running renderer on the status bar.</summary>
    public string BackendName => Backend switch
    {
        PreviewBackend.Gpu when active is GpuPreviewSurface { EightBitFeedback: true } => "GPU, 8-bit feedback",
        PreviewBackend.Gpu => "GPU",
        _ => "CPU",
    };

    /// <summary>Cost of the last frame, for the status readout.</summary>
    public double FrameMilliseconds => active.FrameMilliseconds;

    public double Time
    {
        get => active.Time;
        set => active.Time = value;
    }

    public Func<double>? Clock
    {
        get => active.Clock;
        set => active.Clock = value;
    }

    public PixelSize Resolution
    {
        get => active.Resolution;
        set => active.Resolution = value;
    }

    public CompiledPatch Program
    {
        get => active.Program;
        set => active.Program = value;
    }

    public void Rewind() => active.Rewind();

    /// <summary>Switches renderer, or does nothing if that one is already running.</summary>
    public void Use(PreviewBackend backend)
    {
        if (Backend == backend) return;
        if (backend == PreviewBackend.Gpu && !GpuAvailable) return;

        Switch(backend);
    }

    /// <summary>
    /// Puts a renderer in place, whatever was there before.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Use"/> because that one is allowed to decline —
    /// the same backend twice, or a GPU that has already refused — and the
    /// constructor is not: something has to be in <see cref="active"/> before
    /// anything can read it, and saying so here is what lets every reader after
    /// treat it as a surface rather than as a maybe.
    /// </remarks>
    /// <param name="backend">Which renderer to build and hand the timeline to.</param>
    [MemberNotNull(nameof(active))]
    private void Switch(PreviewBackend backend)
    {
        if (backend == PreviewBackend.Gpu)
        {
            var gpu = new GpuPreviewSurface();
            gpu.Failed += OnGpuFailed;
            Activate(gpu);
        }
        else
        {
            Activate(new PreviewSurface());
        }

        Backend = backend;
    }

    private void OnGpuFailed(string message)
    {
        GpuAvailable = false;

        // Already on the CPU if the person had switched over in the meantime;
        // the offer is still withdrawn, because the reason has not gone away.
        if (Backend == PreviewBackend.Gpu)
        {
            Activate(new PreviewSurface());
            Backend = PreviewBackend.Cpu;
        }

        BackendChanged?.Invoke(message);
    }

    /// <summary>
    /// Puts <paramref name="surface"/> in the tree, carrying over whatever the
    /// outgoing one was showing.
    /// </summary>
    /// <remarks>
    /// The outgoing renderer is dropped rather than kept warm. Both would
    /// otherwise keep ticking, and a preview that is not on screen is a patch
    /// being evaluated for nobody — which on the CPU path is most of a machine.
    /// </remarks>
    /// <param name="surface">The renderer taking over, already built.</param>
    [MemberNotNull(nameof(active))]
    private void Activate<TSurface>(TSurface surface)
        where TSurface : Control, IPreviewSurface
    {
        // Null for the one call that comes from the constructor, and a renderer
        // with a timeline to hand over for every call after it.
        if (active is not null)
        {
            surface.Program = active.Program;
            surface.Resolution = active.Resolution;
            surface.Time = active.Time;
            surface.Clock = active.Clock;

            if (active is GpuPreviewSurface outgoing) outgoing.Failed -= OnGpuFailed;
        }

        active = surface;
        Child = surface;
    }
}
