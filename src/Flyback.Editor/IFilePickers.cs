using Avalonia.Platform.Storage;

namespace Flyback.App;

/// <summary>The system's file pickers, over the editor's window.</summary>
internal interface IFilePickers
{
    Task<IStorageFile?> FromPath(string path);

    Task<IReadOnlyList<IStorageFile>> Open(FilePickerOpenOptions options);

    Task<IStorageFile?> Save(FilePickerSaveOptions options);

    Task<IReadOnlyList<IStorageFolder>> OpenFolder(FolderPickerOpenOptions options);
}
