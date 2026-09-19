using Avalonia.Threading;
using Flyback.App.Audio;
using Flyback.Core.Graph;

namespace Flyback.App;

/// <summary>
/// Hearing a preset from the gallery before picking it: a pointer that rests on a
/// tile plays that preset's sound, quietly and faded in, in place of the patch.
/// </summary>
/// <remarks>
/// The patch is faded out rather than mixed under it, because two patches at once
/// is a sound neither of them makes. Only the sound: the picture on the canvas is
/// the patch's throughout, the tile already showing the preset's own.
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>How long the pointer has to rest on a tile, so sweeping across the gallery plays nothing.</summary>
    private static readonly TimeSpan AuditionDelay = TimeSpan.FromSeconds(1);

    /// <summary>The preset the pointer is resting on, or null.</summary>
    private PatchPreset? pointedAt;

    /// <summary>The wait for <see cref="AuditionDelay"/> to pass, while there is one.</summary>
    private IDisposable? auditionWait;

    /// <summary>
    /// Where the gallery says the pointer is: on a tile, or on none of them for
    /// null — which is also what closing the gallery says.
    /// </summary>
    private void PointedAt(PatchPreset? preset)
    {
        if (preset == pointedAt) return;

        pointedAt = preset;

        auditionWait?.Dispose();
        auditionWait = null;

        StopAuditioning();

        if (preset is not null)
            auditionWait = DispatcherTimer.RunOnce(() => _ = AuditionAsync(preset), AuditionDelay);
    }

    private async Task AuditionAsync(PatchPreset preset)
    {
        // A take records what the speakers play, and a preset tried on the way
        // past is not part of it.
        if (sound.Output is null || audioBlocked || recorder is not null) return;

        AudioEngine.Audition? audition;

        try
        {
            audition = await Task.Run(() => audio.PrepareAudition(preset.Build(plugins.Modules)));
        }
        catch (Exception)
        {
            // A preset that will not build is one that is not heard, which is
            // what its tile already says of its picture.
            return;
        }

        // The pointer may have moved on while it compiled.
        if (audition is null || pointedAt != preset) return;

        // A patch with its Volume down has the device stopped, and the preset
        // needs it running. Started without the preview's clock, which is the
        // patch's to follow, and stopped again once the preset has faded.
        if (!audio.IsRunning)
        {
            try
            {
                audio.Start();
            }
            catch (Exception)
            {
                return;
            }
        }

        audio.StartAudition(audition);
    }

    private void StopAuditioning()
    {
        if (!audio.IsAuditioning) return;

        audio.EndAudition();

        // Whether the device should go on running is the patch's to say again,
        // once the preset has faded out of it.
        DispatcherTimer.RunOnce(
            () =>
            {
                if (!audio.IsAuditioning) SyncAudioToVolume();
            },
            AudioEngine.AuditionFadeOut + TimeSpan.FromMilliseconds(200));
    }
}
