using Flyback.App.Audio;
using Flyback.App.Bars;
using Flyback.App.Controls;
using Flyback.App.Knobs;
using Flyback.Core.Render;

namespace Flyback.App.Settings;

/// <summary>Applies, saves and persists the output settings shown by <see cref="OutputSections"/>.</summary>
internal sealed class OutputSettingsUse(
    OutputSections sections,
    OutputSettingRepository repository,
    EditorSetup setup,
    PreviewHost preview,
    AudioEngine audio,
    PanelKnobs knobs,
    TransportControls transport,
    Playback playback,
    PatchFiles files,
    ReportLine report)
{
    /// <summary>Applies the settings already loaded for this run.</summary>
    public void ApplyCurrent() => Apply(repository.Current);

    /// <summary>Reads the settings controls, applies them and writes them to disk when configured.</summary>
    public void Save()
    {
        var before = repository.Current;
        var saved = repository.Current = sections.Read(before);

        Apply(saved);

        if (saved.LatencyMilliseconds != before.LatencyMilliseconds || sections.SoundChanged(before, saved))
            playback.ReopenAudio(saved);

        if (setup.OutputSettingsPath is not { } path) return;

        try
        {
            saved.Save(path);
        }
        catch (Exception ex)
        {
            report.Say($"Could not save the output settings: {ex.Message}", path);
        }
    }

    private void Apply(OutputSettings settings)
    {
        var size = OutputSections.SizeOf(settings);

        preview.Resolution = size;
        preview.Use(settings.Gpu ? PreviewBackend.Gpu : PreviewBackend.Cpu);
        preview.FrameRate = settings.PreviewFrameRate;

        // A live Scan uses Coordinates' aspect (ADR-0077), so keep it in step
        // with the preview now that sizes are not all the same shape.
        audio.Aspect = SynthRenderer.AspectOf(size.Width, size.Height);

        knobs.Hub.Takeover = settings.Takeover;

        files.UseLibrary(settings.Library);
        files.SoundFolder.FfmpegPath = settings.FfmpegPath;

        if (transport.Overlay is { } overlay) TransportOverlay.Lay(settings.Transport, overlay, knobs.Stage);
        if (transport.PictureWindow is { } picture)
            TransportOverlay.Lay(settings.Transport, picture.Transport, picture.Knobs);
    }
}
