using Avalonia.Platform.Storage;

namespace Flyback.App;

/// <summary>The system's file pickers, over the editor's window.</summary>
internal interface IFilePickers
{
    Task<IReadOnlyList<IStorageFile>> Open(FilePickerOpenOptions options);

    Task<IStorageFile?> Save(FilePickerSaveOptions options);
}