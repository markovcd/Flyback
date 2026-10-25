using Flyback.Gpu;

namespace Flyback.Ui.Controls;

/// <summary>A preview a recording reads its frames from, as they are drawn.</summary>
internal interface ICapturedPreview
{
    /// <summary>Who wants the frames, while a recording lasts. Set from the UI thread.</summary>
    IFrameSink? Capture { get; set; }

    /// <summary>Why frames cannot be read back here, or null when they can.</summary>
    string? CaptureUnavailable { get; }
}
