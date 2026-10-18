using System.Reflection;
using Avalonia.Platform.Storage;

namespace Flyback.Specs.Support;

/// <summary>A file a browser's picker hands a page: a name and bytes, with no path on this machine.</summary>
/// <remarks>
/// A proxy because Avalonia's reference assembly bars <see cref="IStorageFile"/> from user
/// code, and the platform's own files all have a path. Only what a picked file is asked is answered.
/// </remarks>
public class BrowserFile : DispatchProxy
{
    private string name = string.Empty;
    private byte[] bytes = [];
    private readonly Uri path = new($"blob:https://flybackmodular.app/{Guid.NewGuid()}");

    /// <summary>A picked file called <paramref name="name"/> holding <paramref name="bytes"/>.</summary>
    public static IStorageFile Of(string name, byte[] bytes)
    {
        var file = Create<IStorageFile, BrowserFile>();
        var proxy = (BrowserFile)(object)file;

        proxy.name = name;
        proxy.bytes = bytes;

        return file;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        "get_Name" => name,
        "get_Path" => path,
        "get_CanBookmark" => false,
        "OpenReadAsync" => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)),
        "Dispose" => null,
        _ => throw new NotSupportedException($"A page's picked file does not answer {targetMethod?.Name}."),
    };
}
