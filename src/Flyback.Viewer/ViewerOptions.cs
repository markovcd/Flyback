using Avalonia;

namespace Flyback.Viewer;

/// <summary>
/// How one run plays: what the settings file says, overridden by whatever the command
/// line gave. Settled before anything opens, so the window is handed numbers and never
/// asks where they came from.
/// </summary>
internal sealed record ViewerOptions
{
    /// <summary>The patch to play, as a path; null when <see cref="Preset"/> or the startup preset stands in.</summary>
    public string? Patch { get; init; }

    /// <summary>A shipped preset or a saved one, by name.</summary>
    public string? Preset { get; init; }

    /// <summary>Say what <see cref="Preset"/> accepts, and stop.</summary>
    public bool ListPresets { get; init; }

    /// <summary>What the picture is drawn at, which is not the size of the window it is shown in.</summary>
    public PixelSize Size { get; init; } = new(960, 540);

    /// <summary>Preview frames a second; 0 draws as fast as the renderer allows.</summary>
    public double FrameRate { get; init; }

    public bool Gpu { get; init; } = true;

    /// <summary>No picture is compiled and none drawn.</summary>
    public bool NoVideo { get; init; }

    /// <summary>The window's own size, where it is not the picture's.</summary>
    public PixelSize? Window { get; init; }

    public bool Maximized { get; init; }

    public bool FullScreen { get; init; }

    /// <summary>No sound device is opened.</summary>
    public bool NoAudio { get; init; }

    /// <summary>How loud, against what the patch made, from 0 to 1.</summary>
    public float Volume { get; init; } = 1f;

    /// <summary>Starts turned down; the speaker button brings it back.</summary>
    public bool Mute { get; init; }

    public int LatencyMilliseconds { get; init; } = 30;

    /// <summary>Where to start, in seconds.</summary>
    public double From { get; init; }

    /// <summary>Open stopped on the first frame.</summary>
    public bool Paused { get; init; }

    /// <summary>Play this long, then close.</summary>
    public double? For { get; init; }

    /// <summary>Rewind to nought every this many seconds.</summary>
    public double? Loop { get; init; }

    /// <summary>Open without taking focus or coming to the front.</summary>
    public bool Background { get; init; }

    /// <summary>No window at all: the patch plays and nothing appears on screen.</summary>
    public bool Hidden { get; init; }

    /// <summary>Neither the dots nor the toolbar.</summary>
    public bool NoOverlay { get; init; }

    /// <summary>Open with the line saying how the picture is drawn in its corner, which F3 shows and puts away.</summary>
    public bool Stats { get; init; }

    public string? Title { get; init; }

    /// <summary>Keep the window above the others.</summary>
    public bool Top { get; init; }

    /// <summary>Keep the processor's program on the interpreter.</summary>
    public bool Interpreted { get; init; }

    /// <summary>Whether a picture is drawn at all: not when it was turned off, and never with no window to draw it in.</summary>
    public bool Video => !NoVideo && !Hidden;
}
