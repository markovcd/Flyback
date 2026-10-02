using System.Net;
using Flyback.Editor.Assist;
using Flyback.Engine.Compile;
using Flyback.Ui.Audio;
using Flyback.Editor.Bars;
using Flyback.Editor.Canvas;
using Flyback.Editor.Capture;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Files;
using Flyback.Editor.Gallery;
using Flyback.Editor.Inspect;
using Flyback.Editor.Knobs;
using Flyback.Ui.Midi;
using Flyback.Editor.Notices;
using Flyback.Editor.PluginPackages;
using Flyback.Editor.Settings;
using Flyback.Editor.Site;
using Flyback.Editor.Statistics;
using Flyback.Editor.Updates;
using Flyback.Editor.Windows;
using Flyback.Core.Compile;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Flyback.Ui;

namespace Flyback.Editor;

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
    /// <summary>Builds the desktop window around the editor the container composes.</summary>
    public static MainWindow Window(this ServiceProvider provider)
    {
        // Every reactor is built here, so no notice raised later builds one mid-chain.
        var window = provider.GetRequiredService<Reactions>().Building(provider.GetRequiredService<MainWindow>);
        window.Closed += (_, _) => provider.Dispose();
        return window;
    }

    /// <summary>Builds the editor the container composes, for a host that is not a desktop window: a page.</summary>
    public static EditorView View(this ServiceProvider provider) =>
        provider.GetRequiredService<Reactions>().Building(provider.GetRequiredService<EditorView>);

    /// <summary>The container a window is composed in, with any registration <paramref name="replace"/> swaps. Nothing is resolved from it.</summary>
    /// <param name="validate">Whether every registration is checked to be buildable, which costs a walk of the whole graph.</param>
    public static ServiceProvider Provider(EditorSetup? setup = null, Action<IServiceCollection>? replace = null, bool validate = false)
    {
        return Build(new ServiceCollection().AddEditor(setup ?? new EditorSetup()), replace, validate);
    }

    /// <summary>The canvas's services alone, without the rest of the editor.</summary>
    public static ServiceProvider CanvasProvider(Action<IServiceCollection>? replace = null, bool validate = false) =>
        Build(new ServiceCollection().AddCanvas(), replace, validate);

    private static ServiceProvider Build(IServiceCollection services, Action<IServiceCollection>? replace, bool validate)
    {
        replace?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = validate });
    }

    private static IServiceCollection AddEditor(this IServiceCollection services, EditorSetup setup)
    {
        services.AddSingleton(setup.Folders);
        services.AddSingleton(setup.Launch);
        services.AddSingleton(setup.Host);
        services.AddSingleton<IIlCompilerSetup>(setup.Launch);
        services.AddSingleton(setup.Usage);
        services.AddSingleton(setup.Plugins);

        // Long enough for a plugin to download. The site packs a preset with brotli; a page's browser unpacks for itself.
        var site = services.AddHttpClient(SiteAccess.Client, http => http.Timeout = TimeSpan.FromMinutes(5));

        if (!OperatingSystem.IsBrowser())
            site.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });

        services.AddSingleton<IlCompiler>();

        services.AddSingleton<IPresetFolder>(setup.Folders);
        services.AddSingleton<PresetLibrary>();
        services.AddPart<OutputSettingRepository>();
        services.AddPart<SettingsSession>();
        services.AddPart<EditorStart>();

        services.AddSingleton<IStillShelf, FolderStills>();
        services.AddPart<PresetThumbnails>();
        services.AddPart<PresetGallery>();

        services.AddPart<PluginHubFactory>();
        services.AddPart<PluginInstallerFactory>();
        services.AddSingleton(sp => Sound.Open(
            sp.GetRequiredService<PluginCatalog>(),
            sp.GetRequiredService<OutputSettingRepository>().Current));

        services.AddSingleton<IAudioEngine, AudioEngine>();

        // Nothing is opened by this: the backend is asked for a device only once a
        // compiled program is reading one.
        services.AddSingleton(setup.Plugins.PreferredMidiInput);
        services.AddSingleton<MidiHub>();

        services.AddPart<AssistantSettingRepository>();
        services.AddPart<AssistantRunFactory>();
        services.AddSingleton(sp => new Credentials(sp.GetRequiredService<PluginCatalog>().PreferredSecretStore));
        services.AddPart<AssistantConversation>();

        services.AddSingleton<IAssistantEditor, AssistantEditor>();
        services.AddPart<AssistantPanel>();
        services.AddPart<ChosenAssistant>();

        services.AddPart<WorkKeeper>();
        services.AddPart<WindowLayoutKeeper>();

        services.AddPart<ReportLine>();
        services.AddCanvas();
        services.AddPart<SourceView>();
        services.AddPart<PreviewHost>();

        services.AddPart<Document>();
        services.AddSingleton<IDialog, WindowDialog>();
        services.AddSingleton<IFilePickers, WindowFilePickers>();
        services.AddSingleton<IMonitors, WindowMonitors>();
        services.AddSingleton<IFocus, WindowFocus>();
        services.AddSingleton<IClose, WindowClose>();
        services.AddSingleton<ITitle, WindowTitle>();
        services.AddSingleton<IViewer, NoViewer>();
        services.AddPart<EditState>();
        services.AddPart<SiteAccess>();
        services.AddPart<Playback>();
        services.AddPart<RecordingState>();
        services.AddPart<PatchFiles>();
        services.AddPart<PatchViewing>();
        services.AddPart<UnsavedWork>();
        services.AddPart<PatchOpening>();
        services.AddPart<WorkRecovery>();
        services.AddPart<EditorOpened>();
        services.AddPart<UsageCounter>();

        services.AddPart<PictureSection>();
        services.AddPart<SoundSection>();
        services.AddPart<MidiSection>();
        services.AddPart<RecordingSection>();
        services.AddPart<CanvasSection>();
        services.AddPart<FilesSection>();
        services.AddPart<AssistantSection>();
        services.AddPart<UpdatesSection>();
        services.AddPart<UsageSection>();
        services.AddPart<PrivacySection>();

        services.AddPart<PanelKnobs>();
        services.AddPart<KnobRandomizer>();
        services.AddPart<Palette>();
        services.AddPart<Inspector>();
        services.AddPart<PluginInstalls>();
        services.AddPart<PresetAudition>();
        services.AddPart<PresetSlot>();
        services.AddPart<SeekBar>();
        services.AddPart<VolumeSlider>();
        services.AddPart<TransportRow>();
        services.AddPart<Toolbar>();
        services.AddPart<StatusBar>();
        services.AddPart<TakeRecording>();
        services.AddPart<TransportControls>();
        services.AddPart<FullScreenPreview>();
        services.AddPart<ShellLayout>();

        services.AddPart<EditorView>();
        services.AddPart<MainWindow>();
        services.AddPart<WindowHolder>();

        return services;
    }
}
