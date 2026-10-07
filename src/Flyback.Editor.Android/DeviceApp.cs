using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Flyback.Core.Graph;
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

        var plugins = PluginHost.LoadLinked(typeof(DeviceApp).Assembly);

        NodeCatalog.Install(plugins.Modules);

        var provider = EditorServices.Provider(new EditorSetup
        {
            Plugins = plugins,
            Folders = DeviceFolders.Private(),
            Host = new() { PresetSite = Site.PresetSite.Built },
        }, services =>
        {
            services.AddSingleton<ITitle, DeviceTitle>();
            services.AddSingleton<IFocus, DeviceFocus>();
            services.AddSingleton<IClose, DeviceClose>();
        });

        var view = provider.View();
        var opened = false;

        // The activity is a top level only once the view is in it.
        view.AttachedToVisualTree += async (_, e) =>
        {
            if (opened || TopLevel.GetTopLevel(view) is not { } top) return;

            opened = true;
            view.Hold(top);
            view.Start();

            await provider.GetRequiredService<EditorOpened>().RunAsync();
        };

        device.MainView = view;

        base.OnFrameworkInitializationCompleted();
    }
}
