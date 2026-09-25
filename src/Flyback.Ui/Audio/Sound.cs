using Flyback.Core.Graph;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;

namespace Flyback.App.Audio;

/// <summary>The device that was opened, and what it came from — null when nothing could play.</summary>
internal sealed record AudioSetup(IAudioDevice Device, IAudioOutput? Output = null, string? Failure = null);

/// <summary>Opening a sound device and deciding whether a patch wants one, for every shell.</summary>
internal static class Sound
{
    /// <summary>
    /// Opens the best backend the plugins offered. A machine with no sound
    /// plugin, or one whose device refuses to open, gets silence and a disabled
    /// button — never a program that will not start.
    /// </summary>
    public static AudioSetup Open(PluginCatalog plugins, OutputSettings settings)
    {
        if (plugins.PreferredAudioOutput is not { } output)
            return new AudioSetup(new SilentAudioDevice());

        try
        {
            var format = AudioFormat.Default with { LatencyMilliseconds = settings.LatencyMilliseconds };

            return new AudioSetup(output.Create(format, settings.SoundOf(output.Id)), output);
        }
        catch (Exception ex)
        {
            return new AudioSetup(new SilentAudioDevice(), null, $"{output.Name} — {ex.Message}");
        }
    }

    /// <summary>
    /// Whether the Output's own Volume knob says the speakers should be running:
    /// wired, where there is no default left to read and a signal is presumably
    /// meant to be heard, or unwired and above nought — see ADR-0079.
    /// </summary>
    /// <remarks>
    /// Following a panel knob counts as wired, and for the same reason with one
    /// more: the number the socket rests at is not the one playing, and a knob
    /// turning recompiles nothing (ADR-0086), so this is not asked again as it
    /// crosses nought. A fader brought up from the bottom has to find the device
    /// already running.
    /// </remarks>
    public static bool VolumeIsUp(Patch patch) =>
        patch.IncomingTo(patch.Output.Id, NodeCatalog.OutputVolumePort) is not null
        || ControlMap.Of(patch.Output, NodeCatalog.OutputVolumePort) is not null
        || patch.Output.InputValues[NodeCatalog.OutputVolumePort] > 0f;
}
