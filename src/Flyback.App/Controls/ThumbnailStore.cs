using System.IO.Compression;
using Flyback.Core;

namespace Flyback.App.Controls;

/// <summary>
/// Thumbnails kept on disk between runs, a file each, named for what they were
/// drawn from. Never throws: a file that cannot be read or written is a thumbnail
/// drawn again.
/// </summary>
internal sealed class ThumbnailStore(string folder)
{
    public static string DefaultFolder => Path.Combine(GlobalConstants.DataFolder, "thumbnails");

    private const int Format = 1;

    /// <summary>How long a thumbnail nobody has looked at is kept.</summary>
    private static readonly TimeSpan Unused = TimeSpan.FromDays(30);

    private const int PixelBytes = PresetThumbnails.Width * PresetThumbnails.Height * 4;

    /// <summary>The thumbnail kept under <paramref name="key"/>, or null where none is.</summary>
    /// <param name="pixels">False reads only what the patch says, which is all a tile needs before it is in sight.</param>
    public Thumbnail? Find(string key, bool pixels = true)
    {
        var path = PathOf(key);

        try
        {
            if (!File.Exists(path)) return null;

            Thumbnail found;

            using (var file = File.OpenRead(path))
            using (var reader = new BinaryReader(file))
            {
                if (reader.ReadInt32() != Format) return null;

                var drawn = reader.ReadBoolean();
                var words = reader.ReadString();
                var description = Maybe(reader);
                var author = Maybe(reader);
                var count = reader.ReadInt32();
                string[]? tags = count < 0 ? null : [.. Enumerable.Range(0, count).Select(_ => reader.ReadString())];
                byte[]? frame = null;

                if (drawn && pixels)
                {
                    frame = new byte[PixelBytes];

                    using var unpacked = new ZLibStream(file, CompressionMode.Decompress);
                    unpacked.ReadExactly(frame);
                }

                found = new Thumbnail(frame, words, description, author, tags);
            }

            // Looked at, so kept another month.
            if (pixels) File.SetLastWriteTimeUtc(path, DateTime.UtcNow);

            return found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    public void Keep(string key, Thumbnail thumbnail)
    {
        var path = PathOf(key);
        var writing = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(folder);

            using (var file = File.Create(writing))
            using (var writer = new BinaryWriter(file))
            {
                writer.Write(Format);
                writer.Write(thumbnail.Pixels is not null);
                writer.Write(thumbnail.Words);
                Maybe(writer, thumbnail.Description);
                Maybe(writer, thumbnail.Author);
                writer.Write(thumbnail.Tags?.Count ?? -1);

                foreach (var tag in thumbnail.Tags ?? []) writer.Write(tag);

                writer.Flush();

                if (thumbnail.Pixels is { } pixels)
                {
                    using var packed = new ZLibStream(file, CompressionLevel.Fastest, leaveOpen: true);
                    packed.Write(pixels);
                }
            }

            // Written aside and moved in whole, so another Flyback reading it never
            // sees half a file.
            File.Move(writing, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Forget(writing);
        }
    }

    /// <summary>Deletes what has not been looked at in a month, and anything left half written.</summary>
    public void Prune()
    {
        try
        {
            if (!Directory.Exists(folder)) return;

            var now = DateTime.UtcNow;

            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var kept = file.EndsWith(".tmp", StringComparison.Ordinal) ? TimeSpan.FromHours(1) : Unused;

                if (File.GetLastWriteTimeUtc(file) < now - kept) Forget(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string PathOf(string key) => Path.Combine(folder, key + ".thumb");

    private static string? Maybe(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadString() : null;

    private static void Maybe(BinaryWriter writer, string? said)
    {
        writer.Write(said is not null);

        if (said is not null) writer.Write(said);
    }

    private static void Forget(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
