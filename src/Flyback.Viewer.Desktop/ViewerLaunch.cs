using Flyback.Engine.Graph;
using Flyback.Ui.Midi;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Midi;
using Flyback.Host;

namespace Flyback.Viewer.Desktop;

/// <summary>What one run plays: the patch, the device it plays through, what it is played from, and how.</summary>
internal sealed record ViewerLaunch(
    Opened Opened,
    IAudioDevice? Device,
    ViewerOptions Options,
    Takeover Takeover = Takeover.Jump)
{
    /// <summary>What hears the instruments plugged in: the plugins' input, or one that hears none.</summary>
    public IMidiInput Instruments { get; init; } = NoMidiInput.Instance;

    /// <summary>The plugins loaded for the run, whose sound input a Line In listens through.</summary>
    public PluginCatalog Plugins { get; init; } = PluginCatalog.Empty;

    /// <summary>The settings the run plays under, as the file and the command line left them.</summary>
    public OutputSettings Settings { get; init; } = new();

    /// <summary>A sound input of the run's own for a Line In, or null for the plugins' preferred.</summary>
    public IAudioInput? Input { get; init; }

    /// <summary>Whether there is a picture to show: one the patch draws, in a window, on a run that wants video.</summary>
    public bool Pictured => !Options.Hidden && Options.Video && Opened.Patch.Reaches().Picture;
}