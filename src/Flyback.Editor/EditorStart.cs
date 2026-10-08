using Avalonia.Controls;
using Flyback.Core.Graph;
using Flyback.Editor.Files;
using Flyback.Editor.Gallery;
using Flyback.Editor.Knobs;
using Flyback.Editor.Settings;
using Flyback.Editor.Statistics;
using Flyback.Editor.Windows;
using Flyback.Plugins.Hosting;
using Flyback.Ui.Midi;

namespace Flyback.Editor;

/// <summary>Starts the editor run once its window is ready to be shown.</summary>
internal sealed class EditorStart(
    EditorLaunch launch,
    WorkKeeper keeper,
    WindowLayoutKeeper layout,
    MidiHub midi,
    InstrumentLibrary instruments,
    Usage usage,
    PluginCatalog plugins,
    Playback playback,
    PresetSlot presets,
    OutputSettingRepository outputSettings,
    ShellLayout shell,
    ReportLine report)
{
    public void Start(TopLevel host)
    {
        keeper.Start();
        layout.Load();
        if (host is Window window) layout.Apply(window);
        MidiSources.Install(() => [.. midi.Sources.Select(source => source with { Conducts = instruments.For(source)?.Conducts == true })]);
        usage.Started(plugins.Plugins.Select(plugin => plugin.Info.Id), playback.Sound.Output?.Id, ScreenHeights(host));
        presets.StartOn(outputSettings.Current.DefaultPreset);

        if (playback.Sound.Output is null)
            report.Say("No sound backend is installed, so Volume will do nothing. See About for where plugins are looked for.");
        if (launch.Interpreted)
            report.Say($"Running interpreted ({EditorLaunch.InterpretedFlag}): the CPU's programs are not compiled this run.");
        if (launch.WhatsNew is null && launch.OpeningNote is not null) report.Say(launch.OpeningNote);
        shell.ApplyPanelLayout();
    }

    private static IReadOnlyList<int> ScreenHeights(TopLevel host)
    {
        try { return host.Screens?.All.Select(screen => screen.Bounds.Height).ToList() ?? []; }
        catch (Exception) { return []; }
    }
}
