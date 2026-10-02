using Flyback.Core.Render;

namespace Flyback.Editor.Gallery;

/// <summary>The stills in a folder, <see cref="StillIndex.Folder"/> beside the program unless told otherwise.</summary>
internal sealed class FolderStills(string folder) : IStillShelf
{
    public FolderStills()
        : this(Path.Combine(AppContext.BaseDirectory, StillIndex.Folder))
    {
    }

    public async Task<byte[]?> Read(string name)
    {
        var path = Path.Combine(folder, name);

        try
        {
            return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
