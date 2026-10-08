using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Flyback.Core.Graph;
using Flyback.Editor.Gallery;
using Flyback.Plugins.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.Editor.Android;

/// <summary>
/// The editor on a device (ADR-0184): the linked plugins that make modules, and what it
/// keeps in the app's private folder.
/// </summary>
public sealed class DeviceApp : Avalonia.Application
{
    public override void Initialize() => EditorTheme.Apply(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not ISingleViewApplicationLifetime device)
            throw new NotSupportedException("The Android editor runs in an activity.");

        // The activity reads its intent after the application starts, so the editor waits for the activity.
        var host = new Decorator();
        host.AttachedToVisualTree += (_, _) =>
        {
            if (host.Child is null) host.Child = Editor();
        };

        device.MainView = host;

        base.OnFrameworkInitializationCompleted();
    }

    private static EditorView Editor()
    {
        var request = DeviceRequest.Current;
        var plugins = PluginHost.LoadLinked(typeof(DeviceApp).Assembly);

        NodeCatalog.Install(plugins.Modules);

        var provider = EditorServices.Provider(new EditorSetup
        {
            Plugins = plugins,
            Folders = DeviceFolders.Private(),
            Host = new() { PresetSite = Site.PresetSite.Built },
            Launch = new() { Interpreted = request.Interpreted },
        }, services =>
        {
            services.AddSingleton<ITitle, DeviceTitle>();
            services.AddSingleton<IFocus, DeviceFocus>();
            services.AddSingleton<IClose, DeviceClose>();
        });

        var view = provider.View();
        var opened = false;

        view.AttachedToVisualTree += async (_, _) =>
        {
            if (opened || TopLevel.GetTopLevel(view) is not { } top) return;

            opened = true;
            view.Hold(top);
            view.Start();

            await provider.GetRequiredService<EditorOpened>().RunAsync();

            if (request.Preset is { } preset)
                provider.GetRequiredService<PresetSlot>().StartOn(preset);
        };

        return view;
    }
}
