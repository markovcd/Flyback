using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.App.Midi;
using Flyback.App.Statistics;
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

        return services
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true })
            .GetRequiredService<MainWindow>();
    }

    public static IServiceCollection AddEditor(this IServiceCollection services, EditorSetup setup)
    {
        services.AddTransient(typeof(Lazy<>), typeof(Deferred<>));

        services.AddSingleton(setup);
        services.AddSingleton<IIlCompilerSetup>(setup);
        services.AddSingleton(setup.Usage ?? Usage.Off);

        // Read before any window existed, and already installed in the catalog.
        services.AddSingleton(_ => Startup.Plugins);

        // Long enough for a plugin to download.
        services.AddHttpClient(SiteAccess.Client, http => http.Timeout = TimeSpan.FromMinutes(5));

        services.AddSingleton<IlCompiler>();

        // Null for a window that keeps none: every test that did not ask for a folder,
        // which must not see the presets on the machine running it.
        services.AddSingleton(_ => setup.PresetFolder is { } folder ? new PresetLibrary(folder) : null!);

        services.AddSingleton<PresetThumbnails>();

        services.AddSingleton(sp => Sound.Open(
            sp.GetRequiredService<PluginCatalog>(),
            sp.GetRequiredService<OutputSections>().Saved));

        // The device the run opened with; Playback hands the engine any later one.
        services.AddSingleton(sp => sp.GetRequiredService<AudioSetup>().Device);
        services.AddSingleton<AudioEngine>();

        // Nothing is opened by this: the backend is asked for a device only once a
        // compiled program is reading one. Null where no plugin offers one.
        services.AddSingleton(sp => sp.GetRequiredService<PluginCatalog>().PreferredMidiInput!);
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

        services.AddSingleton<MainWindow>();

        return services;
    }

    /// <summary>A service the container builds the first time it is asked for, which is how two that need each other are both built.</summary>
    private sealed class Deferred<T>(IServiceProvider services) : Lazy<T>(services.GetRequiredService<T>)
        where T : notnull;
}
