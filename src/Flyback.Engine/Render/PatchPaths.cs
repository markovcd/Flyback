namespace Flyback.Core.Render;

/// <summary>
/// Where the files a patch names may be looked for, and which of them may leave in a bundle.
/// </summary>
/// <remarks>
/// A patch is a file somebody else wrote. A path naming another machine makes
/// Windows sign in to it the moment it is asked, handing that machine the user's
/// credentials, so one is only followed where the patch itself lives on that
/// share. A bundle carries only a WAV or a PNG, so a patch naming a key or a
/// document cannot smuggle it into a bundle the user then sends on.
/// </remarks>
public static class PatchPaths
{
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// The full path of a file the patch names, measured from <paramref name="beside"/>
    /// when relative, or null where it is on another machine the patch is not on.
    /// </summary>
    public static string? Resolve(string path, string? beside)
    {
        var trimmed = path.Trim();

        if (Remote(trimmed) && !SameShare(trimmed, beside)) return null;

        string full;

        try
        {
            full = beside is { Length: > 0 } && !Path.IsPathRooted(trimmed)
                ? Path.GetFullPath(Path.Combine(beside, trimmed))
                : Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            // Handed back as it came so the complaint quotes what the patch says,
            // and the read that follows fails the ordinary way.
            return trimmed;
        }

        return Remote(full) && !SameShare(full, beside) ? null : full;
    }

    /// <summary>Whether a path names a network share or a device rather than a local file.</summary>
    public static bool Remote(string path) =>
        path.TrimStart().Replace('/', '\\').StartsWith(@"\\", StringComparison.Ordinal);

    /// <summary>
    /// <paramref name="name"/> under <paramref name="folder"/>, or null where it
    /// would land anywhere else.
    /// </summary>
    public static string? Inside(string folder, string name)
    {
        if (Path.IsPathRooted(name) || Remote(name) || name.Contains(':')) return null;

        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, name.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar)));

            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The bytes of a file the patch names, for a bundle to carry: null where it
    /// cannot be read, is on another machine, or is not a WAV or a PNG.
    /// </summary>
    public static byte[]? Carriable(string path, string? beside)
    {
        try
        {
            if (Resolve(path, beside) is not { } full || !File.Exists(full)) return null;

            using var file = File.OpenRead(full);

            var head = new byte[12];

            if (file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < 8 || !Media(head)) return null;

            var bytes = new byte[file.Length];

            file.Position = 0;
            file.ReadExactly(bytes);

            return bytes;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Whether bytes begin as a WAV or a PNG does.</summary>
    public static bool Media(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(PngSignature)
        || (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WAVE"u8));

    private static bool SameShare(string full, string? beside)
    {
        if (beside is not { Length: > 0 }) return false;

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(beside));

            return root is not null
                && Remote(root)
                && string.Equals(
                    Path.GetPathRoot(full)?.Replace('/', '\\'),
                    root.Replace('/', '\\'),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }
}
