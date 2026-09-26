using Avalonia.Platform.Storage;

namespace Flyback.App;

internal sealed class WindowFilePickers(EditorWindow window) : IFilePickers
{
    public Task<IReadOnlyList<IStorageFile>> Open(FilePickerOpenOptions options) =>
        window.Value.StorageProvider.OpenFilePickerAsync(options);

    public Task<IStorageFile?> Save(FilePickerSaveOptions options) =>
        window.Value.StorageProvider.SaveFilePickerAsync(options);
}