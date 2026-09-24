using Avalonia.Controls;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.App;

/// <summary>
/// What the window does with the settings <see cref="OutputSections"/> shows: puts
/// them in force, and writes them out.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// What the Graphics, Recording and Sound sections were last saved as, and so
    /// what closing the settings window without Save puts them back to.
    /// </summary>
    private OutputSettings outputSettings = new();

    /// <summary>Where <see cref="outputSettings"/> is kept, or null to keep it nowhere.</summary>
    private readonly string? outputSettingsPath;

    /// <summary>
    /// Called once, from the constructor, rather than when the settings window
    /// opens: what was last saved has to be in force before anybody has looked.
    /// </summary>
    private void WireOutputControls()
    {
        var gpu = outputSections.Gpu;

        compiler.Failed += message => Dispatcher.UIThread.Post(() => Report(message));

        preview.BackendChanged += message =>
        {
            // The picture's program is only worth compiling while the processor is
            // the one drawing it; the shader has code of its own.
            if (preview.Backend == PreviewBackend.Cpu) compiler.Submit(preview.Program, IlLane.Picture);

            // The choice rather than what is running: a patch the shader cannot
            // draw puts the picture on the processor without anybody having
            // asked, and a box that put itself back to CPU would then be read as
            // the setting having changed — and would be saved as changed the next
            // time anybody pressed Save.
            gpu.SelectedIndex = preview.Wanted == PreviewBackend.Gpu ? 0 : 1;
            gpu.IsEnabled = preview.GpuAvailable;
            ToolTip.SetTip(gpu, preview.GpuAvailable ? OutputSections.GpuTip : message);
            Report(message);
        };

        // The picture a take was reading has gone. Finishing the file is the only
        // useful thing left to do with it — what is already written is a
        // recording, and what would follow is the same frame for ever.
        preview.CaptureLost += Recording.Stop;

        // Shown while it is grayed out too, because a disabled control that will
        // not say why is the most annoying thing a panel can contain.
        ToolTip.SetShowOnDisabled(recordButton, true);

        recordButton.Click += async (_, _) => await ToggleRecordAsync();

        BuildMidiSection();

        // Quietly, because nobody asked for anything yet: a saved answer is
        // what the program starts in, not a change to report.
        outputSections.Show(outputSettings);
        UseOutputSettings(outputSettings);
    }

    /// <summary>
    /// Hands <paramref name="settings"/> to the preview and the sound. The only way
    /// anything in the Graphics section reaches either.
    /// </summary>
    private void UseOutputSettings(OutputSettings settings)
    {
        var size = OutputSections.SizeOf(settings);

        preview.Resolution = size;
        preview.Use(settings.Gpu ? PreviewBackend.Gpu : PreviewBackend.Cpu);
        preview.FrameRate = settings.PreviewFrameRate;

        // What a live Scan reaches with Coordinates' aspect (ADR-0077) — kept in
        // step with the preview rather than fixed, now that the size list is not
        // all one shape.
        audio.Aspect = SynthRenderer.AspectOf(size.Width, size.Height);

        controls.Takeover = settings.Takeover;
    }

    /// <summary>
    /// Takes what the sections hold as the settings, puts them in force, and writes
    /// them out when there is somewhere to. A failure to write is said, not thrown:
    /// they are in force for this run regardless.
    /// </summary>
    private void SaveOutputSettings()
    {
        var before = outputSettings;

        outputSettings = outputSections.Read(before);

        UseOutputSettings(outputSettings);

        if (outputSettings.LatencyMilliseconds != before.LatencyMilliseconds || outputSections.SoundChanged(before, outputSettings))
            playback.ReopenAudio(outputSettings);

        if (outputSettingsPath is null) return;

        try
        {
            outputSettings.Save(outputSettingsPath);
        }
        catch (Exception ex)
        {
            Report($"Could not save the output settings: {ex.Message}", outputSettingsPath);
        }
    }

    /// <summary>
    /// Picks the startup patch from the gallery the toolbar opens, with nothing in
    /// it to save or delete: what is chosen here is a name, and Cancel drops it.
    /// </summary>
    private async Task<string?> PickStartupPatchAsync(string current)
    {
        var showing = OrderedPresets().FirstOrDefault(preset => preset.Name == current);

        var gallery = PresetGallery.Build(
            [.. plugins.Presets.OrderBy(p => p.Kind)],
            showing,
            thumbnails,
            audition.PointedAt,
            Yours()?.ToPickFrom());

        var chosen = await this.ShowDialog<PatchPreset?>("Startup patch", gallery.Tiles, gallery.Filter, fill: true);

        audition.PointedAt(null);

        return chosen?.Name;
    }
}
