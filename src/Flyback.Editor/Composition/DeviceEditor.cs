using System.Reflection;
using Flyback.Editor.Gallery;
using Flyback.Editor.Shell;
using Flyback.Plugins.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.Editor.Composition;

/// <summary>
/// The editor on a phone or tablet (ADR-0184): the plugins linked into the app, what it
/// keeps in the app's own folder, and every press a finger's.
/// </summary>
internal static class DeviceEditor
{
    /// <param name="app">The app's assembly, whose linked plugins make modules.</param>
    /// <param name="preset">A shipped preset to start on once the editor is open, as a script launching the app asks; null for the usual one.</param>
    /// <param name="started">Handed the container as soon as it is built, for the app's own readout.</param>
    public static EditorView Build(Assembly app, EditorFolders folders, string? preset, bool interpreted, Action<IServiceProvider> started)
    {
        var plugins = PluginHost.LoadLinked(app).Install();

        var provider = EditorServices.Provider(new EditorSetup
        {
            Plugins = plugins,
            Folders = folders,
            Host = new() { PresetSite = Site.PresetSite.Built },
            Launch = new() { Interpreted = interpreted },
        }, services =>
        {
            services.AddSingleton<ITitle, DeviceTitle>();
            services.AddSingleton<IFocus, DeviceFocus>();
            services.AddSingleton<IClose, DeviceClose>();
        });

        // Every press on a device is a finger's, its keys the on-screen keyboard's.
        provider.GetRequiredService<LastPress>().OnlyFingers();
        started(provider);

        var view = provider.View();

        view.StartWhenShown(() =>
        {
            if (preset is not null) provider.GetRequiredService<PresetSlot>().StartOn(preset);
        });

        return view;
    }
}
