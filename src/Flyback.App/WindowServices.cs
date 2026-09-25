using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Flyback.App.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.App;

/// <summary>Questions put over the editor's window, to be answered before anything else happens.</summary>
internal interface IDialogs
{
    /// <inheritdoc cref="Dialog.extension(Window).ShowDialog{TResult}(string, Control, Control?, bool)"/>
    Task<TResult> Show<TResult>(string title, Control content, Control? header = null, bool fill = false);
}

/// <summary>The system's file pickers, over the editor's window.</summary>
internal interface IFilePickers
{
    Task<IReadOnlyList<IStorageFile>> Open(FilePickerOpenOptions options);

    Task<IStorageFile?> Save(FilePickerSaveOptions options);
}

/// <summary>The monitors plugged in now.</summary>
internal interface IMonitors
{
    IReadOnlyList<Screen> All { get; }
}

/// <summary>Whether the editor's window is the one in front.</summary>
internal interface IWindowFocus
{
    bool IsActive { get; }
}

/// <summary>Closes the editor's window.</summary>
internal interface IWindowClose
{
    void Close();
}

/// <summary>
/// The editor's window, for the services that act on it. The window is built from
/// what takes these, so it is asked for only once one of them is used.
/// </summary>
internal sealed class EditorWindow(IServiceProvider services)
{
    private bool building;

    /// <summary>The window. Asked for while it is being built, it throws rather than have the container build a second one.</summary>
    public MainWindow Value => building
        ? throw new InvalidOperationException("The window was asked for while it was being built. Ask for it once something acts, not from a constructor.")
        : services.GetRequiredService<MainWindow>();

    /// <summary>Builds the window, with <see cref="Value"/> refused until it is done. The container's one way to a <see cref="MainWindow"/>.</summary>
    public MainWindow Build()
    {
        building = true;

        try
        {
            return ActivatorUtilities.CreateInstance<MainWindow>(services);
        }
        finally
        {
            building = false;
        }
    }
}

internal sealed class WindowDialogs(EditorWindow window) : IDialogs
{
    public Task<TResult> Show<TResult>(string title, Control content, Control? header = null, bool fill = false) =>
        window.Value.ShowDialog<TResult>(title, content, header, fill);
}

internal sealed class WindowFilePickers(EditorWindow window) : IFilePickers
{
    public Task<IReadOnlyList<IStorageFile>> Open(FilePickerOpenOptions options) =>
        window.Value.StorageProvider.OpenFilePickerAsync(options);

    public Task<IStorageFile?> Save(FilePickerSaveOptions options) =>
        window.Value.StorageProvider.SaveFilePickerAsync(options);
}

internal sealed class WindowMonitors(EditorWindow window) : IMonitors
{
    public IReadOnlyList<Screen> All => window.Value.Screens?.All ?? [];
}

internal sealed class WindowFocus(EditorWindow window) : IWindowFocus
{
    public bool IsActive => window.Value.IsActive;
}

internal sealed class WindowClose(EditorWindow window) : IWindowClose
{
    public void Close() => window.Value.Close();
}
