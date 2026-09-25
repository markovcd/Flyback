using Flyback.App.Assist;
using Flyback.App.Audio;
using Flyback.App.Bars;
using Flyback.App.Canvas;
using Flyback.App.Capture;
using Flyback.App.Controls;
using Flyback.App.Files;
using Flyback.App.Gallery;
using Flyback.App.Inspect;
using Flyback.App.Knobs;
using Flyback.App.Midi;
using Flyback.App.PluginPackages;
using Flyback.App.Settings;
using Flyback.App.Site;
using Flyback.App.Statistics;
using Flyback.App.Updates;
using Flyback.Core.Compile;
using Flyback.Plugins.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.App;

/// <summary>
/// The editor's composition root: every hub, region and service of one window, and
/// the window itself, registered in one container (ADR-0150).
/// </summary>
/// <remarks>
/// One container per window, so everything is a singleton of it, and every class of
/// the editor's own is built from its constructor. Where two need each other, one
/// takes a <see cref="Lazy{T}"/> of the other and asks for it only once it acts. The
/// factories below are for what is not the editor's own, which the viewer and the
/// tests build by hand as well: the sound and a value that may be absent.
/// </remarks>
internal static class EditorServices
{
    /// <summary>Builds the window <paramref name="setup"/> describes, with any registration <paramref name="replace"/> swaps.</summary>
    /// <param name="replace">Registers a service a second time, which wins: a test's own site, say.</param>
    public static MainWindow Window(EditorSetup? setup = null, Action<IServiceCollection>? replace = null)
    {
        var services = new ServiceCollection().AddEditor(setup ?? new EditorSetup());
        replace?.Invoke(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var window = provider.GetRequiredService<MainWindow>();

        // After OnClosed has finished the take: the engine, the compiler and MIDI go with the container.
        window.Closed += (_, _) => provider.Dispose();

        return window;
    }

    public static IServiceCollection AddEditor(this IServiceCollection services, EditorSetup setup)
    {
        services.AddTransient(typeof(Lazy<>), typeof(Deferred<>));

        services.AddSingleton(setup);
        services.AddSingleton<IIlCompilerSetup>(setup);
        services.AddSingleton(setup.Usage);
        services.AddSingleton(setup.Plugins);

        // Long enough for a plugin to download.
        services.AddHttpClient(SiteAccess.Client, http => http.Timeout = TimeSpan.FromMinutes(5));

        services.AddSingleton<IlCompiler>();

        services.AddSingleton<IPresetFolder>(setup);
        services.AddSingleton<PresetLibrary>();

        services.AddSingleton<PresetThumbnails>();

        services.AddSingleton(sp => Sound.Open(
            sp.GetRequiredService<PluginCatalog>(),
            sp.GetRequiredService<OutputSections>().Saved));

        services.AddSingleton<AudioEngine>();

        // Nothing is opened by this: the backend is asked for a device only once a
        // compiled program is reading one.
        services.AddSingleton(setup.Plugins.PreferredMidiInput);
        services.AddSingleton<MidiHub>();

        services.AddSingleton<IAssistantEditor, AssistantEditor>();
        services.AddSingleton<AssistantPanel>();

        services.AddSingleton<WorkKeeper>();

        services.AddSingleton<ReportLine>();
        services.AddCanvas();
        services.AddSingleton<SourceView>();
        services.AddSingleton<PreviewHost>();

        services.AddSingleton<Document>();
        services.AddSingleton<IDialogs, WindowDialogs>();
        services.AddSingleton<IFilePickers, WindowFilePickers>();
        services.AddSingleton<IMonitors, WindowMonitors>();
        services.AddSingleton<IWindowFocus, WindowFocus>();
        services.AddSingleton<IWindowClose, WindowClose>();
        services.AddSingleton<SiteAccess>();
        services.AddSingleton<Playback>();
        services.AddSingleton<PatchFiles>();
        services.AddSingleton<UnsavedWork>();

        services.AddSingleton<OutputSections>();
        services.AddSingleton<CanvasSection>();
        services.AddSingleton<UpdatesSection>();
        services.AddSingleton<UsageSection>();
        services.AddSingleton<FilesSection>();

        services.AddSingleton<PanelKnobs>();
        services.AddSingleton<Palette>();
        services.AddSingleton<Inspector>();
        services.AddSingleton<PluginInstalls>();
        services.AddSingleton<PresetAudition>();
        services.AddSingleton<PresetSlot>();
        services.AddSingleton<SeekBar>();
        services.AddSingleton<Toolbar>();
        services.AddSingleton<StatusBar>();
        services.AddSingleton<TakeRecording>();

        // Through its guard, so a service that asks for the window while it is built is refused.
        services.AddSingleton<EditorWindow>();
        services.AddSingleton(sp => sp.GetRequiredService<EditorWindow>().Build());

        return services;
    }

    /// <summary>A service the container builds the first time it is asked for, which is how two that need each other are both built.</summary>
    private sealed class Deferred<T>(IServiceProvider services) : Lazy<T>(services.GetRequiredService<T>)
        where T : notnull;
}
