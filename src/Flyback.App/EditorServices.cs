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
/// tests build by hand as well: the compiler, the sound, MIDI, the thumbnails, the
/// assistant's column and a value that may be absent.
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
        services.AddSingleton(setup.Usage ?? Usage.Off);

        // Read before any window existed, and already installed in the catalog.
        services.AddSingleton(_ => Startup.Plugins);

        services.AddKeyedSingleton(SiteAccess.Client, (_, _) => SiteAccess.Shared);

        // Before anything is compiled, so no build is started only to be taken off.
        services.AddSingleton(_ => new IlCompiler { Enabled = !setup.Interpreted });

        // Null for a window that keeps none: every test that did not ask for a folder,
        // which must not see the presets on the machine running it.
        services.AddSingleton(_ => setup.PresetFolder is { } folder ? new PresetLibrary(folder) : null!);

        services.AddSingleton(sp => new PresetThumbnails(
            sp.GetRequiredService<PluginCatalog>().Modules,
            sp.GetRequiredService<IlCompiler>(),
            setup.ThumbnailFolder)
        {
            Saved = sp.GetService<PresetLibrary>(),
        });

        services.AddSingleton(sp => Sound.Open(
            sp.GetRequiredService<PluginCatalog>(),
            sp.GetRequiredService<OutputSections>().Saved));

        services.AddSingleton(sp => new AudioEngine(sp.GetRequiredService<AudioSetup>().Device)
        {
            Compiler = sp.GetRequiredService<IlCompiler>(),
        });

        // Nothing is opened by this: the backend is asked for a device only once a
        // compiled program is reading one.
        services.AddSingleton(sp => new MidiHub(sp.GetRequiredService<PluginCatalog>().PreferredMidiInput));

        services.AddSingleton(AssistantPanel);

        // Null where no unsaved work is kept, which is every test.
        services.AddSingleton(sp => setup.RecoveryFolder is { } folder
            ? new WorkKeeper(folder, sp.GetRequiredService<UnsavedWork>().Work)
            : null!);

        services.AddSingleton<ReportLine>();
        services.AddCanvas();
        services.AddSingleton<SourceView>();
        services.AddSingleton<PreviewHost>();

        services.AddSingleton<Document>();
        services.AddSingleton<Shell>();
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
        services.AddSingleton<Toolbar>();
        services.AddSingleton<StatusBar>();
        services.AddSingleton<TakeRecording>();

        services.AddSingleton<MainWindow>();

        return services;
    }

    /// <summary>The assistant's column, closed until the toolbar opens it.</summary>
    private static AssistantPanel AssistantPanel(IServiceProvider services)
    {
        var editor = services.GetRequiredService<NodeEditor>();
        var document = services.GetRequiredService<Document>();
        var preview = services.GetRequiredService<PreviewHost>();
        var report = services.GetRequiredService<ReportLine>();
        var files = services.GetRequiredService<PatchFiles>();
        var presets = services.GetRequiredService<Lazy<PresetSlot>>();

        return new AssistantPanel(
            services.GetRequiredService<PluginCatalog>(),
            () => editor.History.Patch,
            // An edit rather than a new document, so it undoes like every other edit
            // and there is nothing to ask about first.
            patch =>
            {
                document.TakeFromAssistant(patch);
                preview.Rewind();
            },
            (message, detail) => report.Say(message, detail),
            samples: files.Sounds,
            pictures: files.Pictures,
            asked: services.GetRequiredService<Usage>().Assistant,
            presets: () => presets.Value.Ordered())
        {
            IsVisible = false,
        };
    }

    /// <summary>A service the container builds the first time it is asked for, which is how two that need each other are both built.</summary>
    private sealed class Deferred<T>(IServiceProvider services) : Lazy<T>(services.GetRequiredService<T>)
        where T : notnull;
}
