using Avalonia.Threading;
using Flyback.App.Audio;
using Flyback.App.Capture;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.App.Gallery;

/// <summary>
/// Trying a preset from the gallery before picking it: a pointer that rests on a
/// tile plays that preset's sound, faded in, in place of the patch, and plays its
/// picture on the tile.
/// </summary>
/// <remarks>
/// The patch is faded out rather than mixed under it, because two patches at once
/// is a sound neither of them makes. The picture on the canvas is the patch's
/// throughout, the preset's own moving on its tile instead.
/// <para>
/// Nothing of the window's document is touched, which is what makes this its own
/// thing: a preset is built from the gallery's own library and played through the
/// engine, and the one thing handed back is that the patch may have the device to
/// itself again.
/// </para>
/// </remarks>
internal sealed class PresetAudition
{
    /// <summary>How long the pointer has to rest on a tile, so sweeping across the gallery plays nothing.</summary>
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(1);

    private readonly AudioEngine audio;
    private readonly IlCompiler compiler;
    private readonly ModuleCatalog modules;

    /// <summary>The presets somebody saved, whose bundles carry what they play.</summary>
    private readonly PresetLibrary saved;

    /// <summary>Whether a preset may be heard at all: there is a device, and no take is reading it.</summary>
    private readonly Func<bool> audible;

    /// <summary>Hands the device back to the patch, which says for itself whether it should run.</summary>
    private readonly Action syncAudio;

    /// <summary>The tile the pointer is resting on, or null.</summary>
    private PointedTile? pointedAt;

    /// <summary>The wait for <see cref="Delay"/> to pass, while there is one.</summary>
    private IDisposable? waiting;

    /// <summary>The preset's picture playing on its tile, while it is being tried.</summary>
    private PresetMotion? motion;

    public PresetAudition(
        AudioEngine audio,
        IlCompiler compiler,
        PluginCatalog plugins,
        PresetLibrary saved,
        Playback playback,
        Lazy<TakeRecording> recording)
    {
        this.audio = audio;
        this.compiler = compiler;
        modules = plugins.Modules;
        this.saved = saved;

        // A take records what the speakers play, and a preset tried on the way past is not part of it.
        audible = () => playback.CanSound && !recording.Value.Running;
        syncAudio = playback.SyncAudioToVolume;
    }

    /// <summary>
    /// Where the gallery says the pointer is: on a tile, or on none of them for
    /// null — which is also what closing the gallery says.
    /// </summary>
    internal void PointedAt(PointedTile? tile)
    {
        if (tile == pointedAt) return;

        pointedAt = tile;

        waiting?.Dispose();
        waiting = null;

        motion?.Dispose();
        motion = null;

        StopAuditioning();

        if (tile is not null)
            waiting = DispatcherTimer.RunOnce(() => _ = AuditionAsync(tile), Delay);
    }

    private async Task AuditionAsync(PointedTile tile)
    {
        // A take records what the speakers play, and a preset tried on the way
        // past is not part of it.
        var hear = audible();

        Opened opened;
        AudioEngine.Audition? audition;

        try
        {
            (opened, audition) = await Task.Run(() =>
            {
                // With what it plays, which for a preset somebody saved is in its bundle.
                var built = PresetLibrary.Open(tile.Preset, saved, modules);
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

        motion = PresetMotion.Play(opened, tile.Picture, clock, compiler: compiler);
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
                if (!audio.IsAuditioning) syncAudio();
            },
            AudioEngine.AuditionFadeOut + TimeSpan.FromMilliseconds(200));
    }
}
