using Avalonia.Platform.Storage;
using Flyback.Editor.Shell;

namespace Flyback.Specs.Support;

/// <summary>A page's file picker, which hands over the one file a scenario has ready and cannot save.</summary>
internal sealed class BrowserPicker : IFilePickers
{
    /// <summary>What the next Open answers with, or null for a canceled picker.</summary>
    public IStorageFile? Ready { get; set; }

    public Task<IStorageFile?> FromPath(string path) => Task.FromResult<IStorageFile?>(null);

    public Task<IReadOnlyList<IStorageFile>> Open(FilePickerOpenOptions options) =>
        Task.FromResult<IReadOnlyList<IStorageFile>>(Ready is { } file ? [file] : []);

    public Task<IStorageFile?> Save(FilePickerSaveOptions options) => Task.FromResult<IStorageFile?>(null);

    public Task<IReadOnlyList<IStorageFolder>> OpenFolder(FolderPickerOpenOptions options) =>
        Task.FromResult<IReadOnlyList<IStorageFolder>>([]);
}
