using Avalonia.Platform.Storage;

namespace Flyback.App.Windows;

internal sealed class WindowFilePickers(WindowHolder holder) : IFilePickers
{
    public Task<IStorageFile?> FromPath(string path) =>
        holder.Instance.StorageProvider.TryGetFileFromPathAsync(path);

    public Task<IReadOnlyList<IStorageFile>> Open(FilePickerOpenOptions options) =>
        holder.Instance.StorageProvider.OpenFilePickerAsync(options);

    public Task<IStorageFile?> Save(FilePickerSaveOptions options) =>
        holder.Instance.StorageProvider.SaveFilePickerAsync(options);
}
