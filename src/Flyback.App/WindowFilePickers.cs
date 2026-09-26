using Avalonia.Platform.Storage;
using Flyback.App.Controls;

namespace Flyback.App;

internal sealed class WindowFilePickers : IFilePickers
{
    public Task<IReadOnlyList<IStorageFile>> Open(FilePickerOpenOptions options) =>
        MainWindowLocator.Owner.StorageProvider.OpenFilePickerAsync(options);

    public Task<IStorageFile?> Save(FilePickerSaveOptions options) =>
        MainWindowLocator.Owner.StorageProvider.SaveFilePickerAsync(options);
}