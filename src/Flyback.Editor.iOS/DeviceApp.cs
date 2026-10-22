using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Flyback.Editor.Composition;
using Flyback.Editor.Gallery;
using Flyback.Editor.Shell;
using Flyback.Plugins.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.Editor.iOS;

/// <summary>
/// The editor on an iPhone or iPad: the linked plugins that make modules, and what it
/// keeps in the app's private folder.
/// </summary>
public sealed class DeviceApp : Avalonia.Application
{
    public override void Initialize() => EditorTheme.Apply(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not ISingleViewApplicationLifetime device)
            throw new NotSupportedException("The iOS editor runs in a single view.");

        device.MainView = Editor();

        base.OnFrameworkInitializationCompleted();
    }

    private static EditorView Editor()
    {
        var request = DeviceRequest.Current;
        var plugins = PluginHost.LoadLinked(typeof(DeviceApp).Assembly).Install();

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

        // Every press on a device is a finger's, its keys the on-screen keyboard's.
        provider.GetRequiredService<LastPress>().OnlyFingers();
        DeviceState.Listen(provider);

        var view = provider.View();
        var opened = false;

        view.AttachedToVisualTree += async (_, _) =>
        {
            if (opened || TopLevel.GetTopLevel(view) is not { } top) return;

            opened = true;
            view.Hold(top);
            view.Start();

            await view.OpenedAsync();

            if (request.Preset is { } preset)
                provider.GetRequiredService<PresetSlot>().StartOn(preset);
        };

        return view;
    }
}
