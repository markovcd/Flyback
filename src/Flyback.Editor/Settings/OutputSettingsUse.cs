using Flyback.App.Audio;
using Flyback.App.Bars;
using Flyback.App.Controls;
using Flyback.App.Files;
using Flyback.App.Knobs;
using Flyback.Core.Render;

namespace Flyback.App.Settings;

/// <summary>Shows, reads, applies and persists the output settings, whichever section each row is in (ADR-0148).</summary>
internal sealed class OutputSettingsUse(
    PictureSection picture,
    SoundSection sound,
    MidiSection midi,
    RecordingSection recording,
    FilesSection fileSection,
    OutputSettingRepository repository,
    EditorFolders folders,
    PreviewHost preview,
    IAudioEngine audio,
    PanelKnobs knobs,
    TransportControls transport,
    Playback playback,
    PatchFiles files,
    ReportLine report)
{
    private readonly IOutputSlice[] slices = [picture, sound, midi, recording, fileSection];

    private readonly LiveOversample live = new(audio, () => repository.Current.StepDownOnDropouts, message => report.Say(message));

    /// <summary>Applies the settings already loaded for this run, while the editor is being built.</summary>
    public void ApplyCurrent()
    {
        Apply(repository.Current, starting: true);
        live.Start();
    }

    /// <summary>Puts every section's rows to what was last saved, and nothing else.</summary>
    public void Show()
    {
        foreach (var slice in slices) slice.Show(repository.Current);
    }

    /// <summary>Reads the settings controls, applies them and writes them to disk when configured.</summary>
    public void Save()
    {
        var before = repository.Current;
        var saved = repository.Current = Read(before);

        Apply(saved);

        if (saved.LatencyMilliseconds != before.LatencyMilliseconds || sound.SoundChanged(before, saved))
            playback.ReopenAudio(saved);

        // One box picks both, and only the CPU takes effect at once.
        if (saved.Driver != before.Driver)
            report.Say($"{(saved.Driver == GraphicsDriver.Direct3D ? "Direct3D" : "OpenGL")} draws from the next time Flyback starts.");

        if (folders.OutputSettingsPath is not { } path) return;

        try
        {
            saved.Save(path);
        }
        catch (Exception ex)
        {
            report.Say($"Could not save the output settings: {ex.Message}", path);
        }
    }

    /// <summary>What the controls hold, as settings to put in force.</summary>
    private OutputSettings Read(OutputSettings before)
    {
        // Set on the knob panel, not in any section.
        var read = new OutputSettings { Randomize = before.Randomize };

        foreach (var slice in slices) slice.Read(read, before);

        return read;
    }

    private void Apply(OutputSettings settings, bool starting = false)
    {
        var size = PictureSection.SizeOf(settings);

        preview.Resolution = size;
        preview.Use(settings.Gpu ? PreviewBackend.Gpu : PreviewBackend.Cpu);
        preview.FrameRate = settings.PreviewFrameRate;

        // A live Scan uses Coordinates' aspect (ADR-0077), so keep it in step
        // with the preview now that sizes are not all the same shape.
        audio.Aspect = SynthRenderer.AspectOf(size.Width, size.Height);
        audio.Oversample = settings.Oversample;

        knobs.Hub.Takeover = settings.Takeover;
        transport.FollowsInstruments = settings.FollowTransport;

        files.UseLibrary(settings.Library, reread: !starting);
        files.SoundFolder.FfmpegPath = settings.FfmpegPath;

        knobs.KnobGrid = settings.KnobGrid;

        if (transport.Overlay is { } overlay) TransportOverlay.Lay(settings.Transport, overlay, knobs.Stage);
        if (transport.PictureWindow is { } picture)
            TransportOverlay.Lay(settings.Transport, picture.Transport, picture.Knobs);
    }
}
