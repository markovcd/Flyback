using Avalonia.Controls;
using Flyback.App.Files;
using Flyback.App.Gallery;
using Flyback.App.Knobs;
using Flyback.App.Midi;
using Flyback.App.Settings;
using Flyback.App.Statistics;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>Starts the editor run once its window is ready to be shown.</summary>
internal sealed class EditorStart(
    EditorSetup setup,
    WorkKeeper keeper,
    WindowLayoutKeeper layout,
    MidiHub midi,
    PanelKnobs knobs,
    Usage usage,
    PluginCatalog plugins,
    Playback playback,
    PresetSlot presets,
    OutputSettingRepository outputSettings,
    ShellLayout shell,
    ReportLine report)
{
    public void Start(Window window)
    {
        keeper.Start();
        layout.Load();
        layout.Apply(window);
        MidiSources.Install(() => [.. midi.Sources.Select(source => source with { Conducts = knobs.Instruments.For(source)?.Conducts == true })]);
        usage.Started(plugins.Plugins.Select(plugin => plugin.Info.Id), playback.Sound.Output?.Id, ScreenHeights(window));
        presets.StartOn(outputSettings.Current.DefaultPreset);

        if (playback.Sound.Output is null)
            report.Say("No sound backend is installed, so Volume will do nothing. See About for where plugins are looked for.");
        if (setup.Interpreted)
            report.Say($"Running interpreted ({Startup.InterpretedFlag}): the CPU's programs are not compiled this run.");
        if (setup.WhatsNew is null && setup.OpeningNote is not null) report.Say(setup.OpeningNote);
        shell.ApplyPanelLayout();
    }

    private static IReadOnlyList<int> ScreenHeights(Window window)
    {
        try { return window.Screens.All.Select(screen => screen.Bounds.Height).ToList(); }
        catch (Exception) { return []; }
    }
}
