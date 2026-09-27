using Avalonia.Platform.Storage;

namespace Flyback.App;

internal sealed class WindowFilePickers(WindowHolder holder) : IFilePickers
{
    public Task<IReadOnlyList<IStorageFile>> Open(FilePickerOpenOptions options) =>
        holder.Instance.StorageProvider.OpenFilePickerAsync(options);

    public Task<IStorageFile?> Save(FilePickerSaveOptions options) =>
        holder.Instance.StorageProvider.SaveFilePickerAsync(options);
}