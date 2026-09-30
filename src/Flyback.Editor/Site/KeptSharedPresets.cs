using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flyback.Core;

namespace Flyback.App.Site;

/// <summary>
/// Every shared preset opened from the site, kept with its file, its still and all the
/// site said of it, so it opens again while the site does not answer. Never throws: a
/// file that cannot be read or written is a preset not kept.
/// </summary>
/// <param name="folder">Where they are kept, or null to keep none.</param>
internal sealed class KeptSharedPresets(string? folder)
{
    public static string DefaultFolder => Path.Combine(GlobalConstants.DataFolder, "shared-presets");

    public static readonly KeptSharedPresets None = new(null);

    private const int Format = 1;

    private const string Extension = ".shared";

    /// <summary>Keeps the preset as just opened, its still kept from before where none came with it.</summary>
    public void Keep(Uri root, SitePreset preset, byte[] file, byte[]? still)
    {
        still ??= Read(PathOf(root, preset.Id), whole: true)?.Still;

        Write(root, preset.Id, preset.Listed, DateTimeOffset.UtcNow, still, file);
    }

    /// <summary>Takes what the site says of a kept preset now, its rating above all; one not kept is left alone.</summary>
    public void Refresh(Uri root, SitePreset seen)
    {
        if (Read(PathOf(root, seen.Id), whole: false) is not { } kept || kept.Listed == seen.Listed) return;

        if (Read(PathOf(root, seen.Id), whole: true) is { File: { } file } whole)
            Write(root, seen.Id, seen.Listed, whole.Opened, whole.Still, file);
    }

    /// <summary>Deletes the kept preset: the site took it down, and nobody gets a copy of it from here.</summary>
    public void Forget(Uri root, string id)
    {
        if (PathOf(root, id) is { } path) Forget(path);
    }

    /// <summary>The preset kept from <paramref name="root"/> under that id, or null where none is.</summary>
    public KeptPreset? Find(Uri root, string id) => Preset(Read(PathOf(root, id), whole: false));

    /// <summary>Every preset kept from <paramref name="root"/>, the last opened first.</summary>
    public IReadOnlyList<KeptPreset> All(Uri root)
    {
        if (folder is null) return [];

        try
        {
            if (!Directory.Exists(folder)) return [];

            return [.. Directory.EnumerateFiles(folder, "*" + Extension)
                .Select(path => Preset(Read(path, whole: false)))
                .OfType<KeptPreset>()
                .Where(kept => kept.Root == root)
                .OrderByDescending(kept => kept.Opened)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The kept preset's file, as the site sent it.</summary>
    public byte[]? File(KeptPreset kept) => Read(PathOf(kept.Root, kept.Preset.Id), whole: true)?.File;

    public byte[]? Still(KeptPreset kept) => Read(PathOf(kept.Root, kept.Preset.Id), whole: true)?.Still;

    private static KeptPreset? Preset(Kept? kept)
    {
        if (kept is null || !Uri.TryCreate(kept.Root, UriKind.Absolute, out var root)) return null;

        try
        {
            using var document = JsonDocument.Parse(kept.Listed);

            return PresetSite.One(document.RootElement, root) is { } preset ? new KeptPreset(root, preset, kept.Opened) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The file kept for one preset of one site, named for a hash of both: an id is the
    /// site's to choose, and never becomes a path.
    /// </summary>
    private string? PathOf(Uri root, string id)
    {
        if (folder is null) return null;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(root.AbsoluteUri + "\n" + id));

        return Path.Combine(folder, Convert.ToHexStringLower(hash.AsSpan(0, 16)) + Extension);
    }

    private void Write(Uri root, string id, string listed, DateTimeOffset opened, byte[]? still, byte[] file)
    {
        if (PathOf(root, id) is not { } path) return;

        var writing = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(folder!);

            using (var stream = System.IO.File.Create(writing))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Format);
                writer.Write(root.AbsoluteUri);
                writer.Write(opened.UtcTicks);
                writer.Write(listed);
                writer.Write(still?.Length ?? -1);
                writer.Write(still ?? []);
                writer.Write(file.Length);
                writer.Write(file);
            }

            // Written aside and swapped in whole, so a reader never sees half a file.
            if (System.IO.File.Exists(path)) System.IO.File.Replace(writing, path, destinationBackupFileName: null);
            else System.IO.File.Move(writing, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Forget(writing);
        }
    }

    /// <param name="whole">False reads only what the site said, which is all a listing needs.</param>
    private static Kept? Read(string? path, bool whole)
    {
        if (path is null) return null;

        try
        {
            if (!System.IO.File.Exists(path)) return null;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = new BinaryReader(stream);

            if (reader.ReadInt32() != Format) return null;

            var root = reader.ReadString();
            var opened = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
            var listed = reader.ReadString();

            if (!whole) return new Kept(root, opened, listed, null, null);

            var stillLength = reader.ReadInt32();
            var still = stillLength < 0 ? null : Bytes(reader, stillLength);
            var file = Bytes(reader, reader.ReadInt32());

            return new Kept(root, opened, listed, still, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>That many bytes, refused where the file does not hold them.</summary>
    private static byte[] Bytes(BinaryReader reader, int length)
    {
        var left = reader.BaseStream.Length - reader.BaseStream.Position;

        if (length < 0 || length > left) throw new InvalidDataException("A kept shared preset is cut short.");

        return reader.ReadBytes(length);
    }

    private static void Forget(string file)
    {
        try
        {
            System.IO.File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record Kept(string Root, DateTimeOffset Opened, string Listed, byte[]? Still, byte[]? File);
}
