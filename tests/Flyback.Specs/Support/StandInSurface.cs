using Avalonia;
using Avalonia.Controls;
using Flyback.Ui.Controls;
using Flyback.Core.Compile;

namespace Flyback.Specs.Support;

/// <summary>A GPU surface that draws nothing, a page's or the desktop's, and fails when told to.</summary>
public sealed class StandInSurface(bool processorStandsIn) : Border, IGpuPreview
{
    public event Action<string>? Failed;

    public string? Api => processorStandsIn ? "OpenGL" : "WebGL";

    public bool ProcessorStandsIn => processorStandsIn;

    public double Time { get; set; }

    public Func<double>? Clock { get; set; }

    public double FramesPerSecond => 0;

    public double FrameMilliseconds => 0;

    public long Frames => 0;

    public double FrameRate { get; set; }

    public PixelSize Resolution { get; set; }

    public CompiledPatch Program { get; set; } = CompiledPatch.Black;

    LiveValues IPreviewSurface.Live { get; set; } = LiveValues.None;

    public void Refresh()
    {
    }

    public void Rewind() => Time = 0;

    public void Fail(string message) => Failed?.Invoke(message);
}
