using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Flyback.App.Controls;

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

/// <remarks>Each takes the window lazily: the window is built from what takes these, and is there by the time one is used.</remarks>
internal sealed class WindowDialogs(Lazy<MainWindow> window) : IDialogs
{
    public Task<TResult> Show<TResult>(string title, Control content, Control? header = null, bool fill = false) =>
        window.Value.ShowDialog<TResult>(title, content, header, fill);
}

internal sealed class WindowFilePickers(Lazy<MainWindow> window) : IFilePickers
{
    public Task<IReadOnlyList<IStorageFile>> Open(FilePickerOpenOptions options) =>
        window.Value.StorageProvider.OpenFilePickerAsync(options);

    public Task<IStorageFile?> Save(FilePickerSaveOptions options) =>
        window.Value.StorageProvider.SaveFilePickerAsync(options);
}

internal sealed class WindowMonitors(Lazy<MainWindow> window) : IMonitors
{
    public IReadOnlyList<Screen> All => window.Value.Screens?.All ?? [];
}

internal sealed class WindowFocus(Lazy<MainWindow> window) : IWindowFocus
{
    public bool IsActive => window.Value.IsActive;
}

internal sealed class WindowClose(Lazy<MainWindow> window) : IWindowClose
{
    public void Close() => window.Value.Close();
}
