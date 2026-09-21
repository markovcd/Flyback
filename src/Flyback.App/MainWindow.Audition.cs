using Avalonia.Threading;
using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.Core.Graph;

namespace Flyback.App;

/// <summary>
/// Trying a preset from the gallery before picking it: a pointer that rests on a
/// tile plays that preset's sound, faded in, in place of the patch, and plays its
/// picture on the tile.
/// </summary>
/// <remarks>
/// The patch is faded out rather than mixed under it, because two patches at once
/// is a sound neither of them makes. The picture on the canvas is the patch's
/// throughout, the preset's own moving on its tile instead.
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>How long the pointer has to rest on a tile, so sweeping across the gallery plays nothing.</summary>
    private static readonly TimeSpan AuditionDelay = TimeSpan.FromSeconds(1);

    /// <summary>The tile the pointer is resting on, or null.</summary>
    private PointedTile? pointedAt;

    /// <summary>The wait for <see cref="AuditionDelay"/> to pass, while there is one.</summary>
    private IDisposable? auditionWait;

    /// <summary>The preset's picture playing on its tile, while it is being tried.</summary>
    private PresetMotion? motion;

    /// <summary>
    /// Where the gallery says the pointer is: on a tile, or on none of them for
    /// null — which is also what closing the gallery says.
    /// </summary>
    private void PointedAt(PointedTile? tile)
    {
        if (tile == pointedAt) return;

        pointedAt = tile;

        auditionWait?.Dispose();
        auditionWait = null;

        motion?.Dispose();
        motion = null;

        StopAuditioning();

        if (tile is not null)
            auditionWait = DispatcherTimer.RunOnce(() => _ = AuditionAsync(tile), AuditionDelay);
    }

    private async Task AuditionAsync(PointedTile tile)
    {
        // A take records what the speakers play, and a preset tried on the way
        // past is not part of it.
        var hear = sound.Output is not null && !audioBlocked && recorder is null;

        Opened opened;
        AudioEngine.Audition? audition;

        try
        {
            (opened, audition) = await Task.Run(() =>
            {
                // With what it plays, which for a preset somebody saved is in its bundle.
                var built = PresetLibrary.Open(tile.Preset, savedPresets, plugins.Modules);
                return (built, hear ? audio.PrepareAudition(built.Patch, built.Samples) : null);
            });
        }
        catch (Exception)
        {
            // A preset that will not build is one that is not tried, which is
            // what its tile already says of its picture.
            return;
        }

        // The pointer may have moved on while it compiled.
        if (pointedAt != tile) return;

        Func<double>? clock = null;

        if (audition is not null && DeviceRunning())
        {
            audio.StartAudition(audition);
            clock = () => audition.Time;
        }

        motion = PresetMotion.Play(opened, tile.Picture, clock);
    }

    /// <summary>
    /// Whether the device is running, starting it if it was not. A patch with its
    /// Volume down has it stopped, and the preset needs it. Started without the
    /// preview's clock, which is the patch's to follow, and stopped again once the
    /// preset has faded.
    /// </summary>
    private bool DeviceRunning()
    {
        if (audio.IsRunning) return true;

        try
        {
            audio.Start();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
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
