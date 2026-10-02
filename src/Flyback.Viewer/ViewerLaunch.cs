using Flyback.Core.Graph;
using Flyback.Ui.Midi;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Midi;

namespace Flyback.Viewer;

/// <summary>What one run plays: the patch, the device it plays through, what it is played from, and how.</summary>
internal sealed record ViewerLaunch(
    Opened Opened,
    IAudioDevice? Device,
    ViewerOptions Options,
    Takeover Takeover = Takeover.Jump)
{
    /// <summary>What hears the instruments plugged in: the plugins' input, or one that hears none.</summary>
    public IMidiInput Instruments { get; init; } = NoMidiInput.Instance;

    /// <summary>Whether there is a picture to show: one the patch draws, in a window, on a run that wants video.</summary>
    public bool Pictured => !Options.Hidden && Options.Video && Opened.Patch.Reaches().Picture;
}