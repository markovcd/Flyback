using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Flyback.Editor;
using Flyback.Ui.Audio;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Gallery;
using Flyback.Core.Graph;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.Editor.Web;

/// <summary>
/// The editor in a page (ADR-0162): the shipped plugins that make modules, nothing kept
/// between visits, the picture on a canvas of its own, and the site it is served from.
/// </summary>
internal sealed class PageApp : Application
{
    public override void Initialize() => EditorTheme.Apply(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not ISingleViewApplicationLifetime page)
            throw new NotSupportedException("The web editor runs in a page.");

        var plugins = PluginHost.LoadLinked(typeof(PageApp).Assembly);

        NodeCatalog.Install(plugins.Modules);

        var provider = EditorServices.Provider(new EditorSetup { Plugins = plugins, Host = new() { InPage = true, PresetSite = PageSite.Root, Home = PageSite.Home } }, services =>
        {
            services.AddSingleton<Func<IGpuPreview>>(sp => () => new CanvasPreview(sp.GetRequiredService<IDialog>()));
            services.AddSingleton<ITitle, PageTitle>();
            services.AddSingleton<IFocus, PageFocus>();
            services.AddSingleton<IClose, PageClose>();
            services.AddSingleton<IStillShelf, PageStills>();
            services.AddSingleton<PageViewer>();
            services.AddSingleton<IViewer>(sp => sp.GetRequiredService<PageViewer>());
            services.AddSingleton<IAudioEngine, PageSound>();
            services.AddSingleton(new AudioSetup(new SilentAudioDevice(), new PageSpeakers()));
        });

        var view = provider.View();
        var opened = false;

        // The page is a top level only once the view is in it.
        view.AttachedToVisualTree += async (_, e) =>
        {
            if (opened || TopLevel.GetTopLevel(view) is not { } top) return;

            opened = true;
            view.Hold(top);
            view.Start();

            await provider.GetRequiredService<EditorOpened>().RunAsync();
        };

        page.MainView = view;
        PageExports.Provider = provider;

        base.OnFrameworkInitializationCompleted();
    }
}
