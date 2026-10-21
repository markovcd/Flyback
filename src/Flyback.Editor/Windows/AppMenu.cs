using Avalonia.Controls;
using Avalonia.Input;
using Flyback.Core;
using Flyback.Editor.Controls;
using Flyback.Editor.Notices;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.Editor.Windows;

/// <summary>
/// The menu macOS shows under the program's name in the menu bar. Avalonia adds
/// Services, Hide, Hide Others, Show All and Quit below these.
/// </summary>
/// <remarks>
/// Made before the editor, since macOS reads it as the application starts; each item
/// does nothing until <see cref="Serve"/> names the editor it works on.
/// </remarks>
internal sealed class AppMenu
{
    private IServiceProvider? editor;

    public NativeMenu Menu { get; } = new();

    public AppMenu()
    {
        Menu.Add(Item($"About {GlobalConstants.ApplicationName}", About.Show));
        Menu.Add(new NativeMenuItemSeparator());
        Menu.Add(Item("Settings…", Raising(new SettingsAsked()), new KeyGesture(Key.OemComma, KeyModifiers.Meta)));
        Menu.Add(Item("Plugins…", Raising(new PluginsAsked())));
    }

    /// <summary>Points every item at <paramref name="services"/>, the editor's container.</summary>
    public void Serve(IServiceProvider services) => editor = services;

    private Func<IDialog, Task> Raising<T>(T notice) where T : INotice => _ =>
    {
        editor?.GetRequiredService<Reactions>().Raise(notice);
        return Task.CompletedTask;
    };

    /// <summary>An item that does nothing while a dialog is up, as the toolbar under it cannot be clicked then.</summary>
    private NativeMenuItem Item(string header, Func<IDialog, Task> act, KeyGesture? gesture = null)
    {
        var item = new NativeMenuItem(header) { Gesture = gesture };

        item.Click += async (_, _) =>
        {
            if (editor?.GetRequiredService<IDialog>() is { IsShowing: false } dialog) await act(dialog);
        };

        return item;
    }
}
