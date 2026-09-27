using Avalonia.Platform.Storage;
using Flyback.App.Controls;

namespace Flyback.App;

internal sealed class WindowFilePickers(MainWindowLocator locator) : IFilePickers
{
    public Task<IReadOnlyList<IStorageFile>> Open(FilePickerOpenOptions options) =>
        locator.Owner.StorageProvider.OpenFilePickerAsync(options);

    public Task<IStorageFile?> Save(FilePickerSaveOptions options) =>
        locator.Owner.StorageProvider.SaveFilePickerAsync(options);
}