using System.Reflection;
using Avalonia.Platform.Storage;

namespace Flyback.Editor.Tests;

/// <summary>Files on disk as the window is handed them by a drop or a save dialog.</summary>
/// <remarks>
/// <see cref="IStorageFile"/> may not be implemented outside Avalonia, and the headless
/// platform's storage provider finds no files, so this builds the platform's own
/// file-backed implementation, whose type is internal.
/// </remarks>
internal static class StorageFiles
{
    internal static IStorageFile OnDisk(string path)
    {
        var type = typeof(IStorageFile).Assembly.GetType(
            "Avalonia.Platform.Storage.FileIO.BclStorageFile", throwOnError: true)!;

        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];

        return (IStorageFile)ctor.Invoke([new FileInfo(path)]);
    }
}
