using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Picture;

namespace Flyback.App.Tests.PluginPackages;

/// <summary>Plugin packages built in memory, one zip entry at a time.</summary>
internal static class Packages
{
    /// <summary>A real plugin assembly, built against the contract this Flyback offers, that adds modules and presets.</summary>
    public static byte[] Assembly { get; } = File.ReadAllBytes(typeof(PicturePlugin).Assembly.Location);

    public const string AssemblyName = "Flyback.Plugins.Picture.dll";

    /// <summary>The folder the picture plugin is installed into.</summary>
    public const string Folder = "Flyback.Plugins.Picture";

    /// <summary>The sample plugin, whose project sets its tags and embeds a preview.</summary>
    public static byte[] Sample { get; } = File.ReadAllBytes(typeof(Flyback.Plugins.Sample.SampleModulesPlugin).Assembly.Location);

    /// <summary>The key test packages are signed with.</summary>
    public static ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>Somebody else's key.</summary>
    public static ECDsa OtherKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>A signature is different every time, so each package is signed once and kept.</summary>
    private static readonly ConcurrentDictionary<string, byte[]> Signed = new();

    /// <summary>A package with the sample plugin for Windows, signed with <see cref="Key"/>.</summary>
    public static byte[] ForSample() => Signed.GetOrAdd("sample", _ => Sign(Zip([("win/Flyback.Plugins.Sample.dll", Sample)])));

    public static byte[] Sign(byte[] package, ECDsa? key = null) => PackageSigner.Sign(package, key ?? Key);

    /// <summary>An assembly a plugin might carry beside it, whose code reaches the network.</summary>
    public static byte[] Networking { get; } = File.ReadAllBytes(typeof(System.Net.Http.HttpClient).Assembly.Location);

    /// <summary>A package with the picture plugin built for each of <paramref name="platforms"/>, signed with <see cref="Key"/>.</summary>
    public static byte[] For(params string[] platforms) =>
        Signed.GetOrAdd(string.Join(",", platforms), _ => Sign(Unsigned(platforms)));

    /// <summary>A package with the picture plugin built for each of <paramref name="platforms"/>, signed by nobody.</summary>
    public static byte[] Unsigned(params string[] platforms) => Unsigned(Assembly, platforms);

    public static byte[] Unsigned(byte[] assembly, params string[] platforms)
    {
        var entries = new List<(string, byte[])>();

        foreach (var platform in platforms)
        {
            entries.Add(($"{platform}/{AssemblyName}", assembly));
            entries.Add(($"{platform}/Flyback.Plugins.Picture.deps.json", Encoding.UTF8.GetBytes("{}")));
            entries.Add(($"{platform}/runtimes/{platform}/native/readme.txt", [1, 2, 3]));
        }

        return Zip(entries);
    }

    public static byte[] Zip(IEnumerable<(string Name, byte[] Bytes)> entries)
    {
        using var memory = new MemoryStream();

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                var entry = zip.CreateEntry(name);

                // Fixed, so the same entries always zip to the same bytes and the same hash.
                entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

                using var stream = entry.Open();
                stream.Write(bytes);
            }
        }

        return memory.ToArray();
    }

    /// <summary>
    /// A copy of <paramref name="image"/> that says it was compiled against
    /// <paramref name="version"/> of <paramref name="reference"/>, which is how an old or
    /// a future plugin looks without one having been built.
    /// </summary>
    public static byte[] BuiltAgainst(byte[] image, string reference, Version version)
    {
        var copy = (byte[])image.Clone();

        using var reader = new PEReader(new MemoryStream(image));

        var metadata = reader.GetMetadataReader();
        var table = reader.PEHeaders.MetadataStartOffset + metadata.GetTableMetadataOffset(TableIndex.AssemblyRef);
        var size = metadata.GetTableRowSize(TableIndex.AssemblyRef);

        foreach (var handle in metadata.AssemblyReferences)
        {
            if (metadata.GetString(metadata.GetAssemblyReference(handle).Name) != reference) continue;

            // A row starts with the version: major, minor, build and revision, two bytes each.
            var row = table + (MetadataTokens.GetRowNumber(handle) - 1) * size;

            BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(row), (ushort)version.Major);
            BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(row + 2), (ushort)version.Minor);
            BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(row + 4), (ushort)Math.Max(0, version.Build));
            BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(row + 6), (ushort)Math.Max(0, version.Revision));

            return copy;
        }

        throw new InvalidOperationException($"it does not reference {reference}");
    }

    /// <summary>The picture plugin as an older release of it: every digit of its version a nought.</summary>
    public static byte[] Older { get; } = Versioned(Assembly, '0');

    /// <summary>The picture plugin as a newer release of it: every digit of its version a nine.</summary>
    public static byte[] Newer { get; } = Versioned(Assembly, '9');

    /// <summary>
    /// A copy of <paramref name="image"/> with every digit of its informational
    /// version turned to <paramref name="digit"/>, which keeps the attribute's length.
    /// </summary>
    private static byte[] Versioned(byte[] image, char digit)
    {
        var version = PluginDescription.Of([(AssemblyName, () => new MemoryStream(image))])!.Version;
        var current = Encoding.UTF8.GetBytes(version);
        var wanted = Encoding.UTF8.GetBytes(new string([.. version.Select(c => char.IsAsciiDigit(c) ? digit : c)]));

        var copy = (byte[])image.Clone();
        var at = copy.AsSpan().IndexOf(current);

        if (at < 0) throw new InvalidOperationException("it has no informational version");

        wanted.CopyTo(copy.AsSpan(at));

        return copy;
    }

    /// <summary>The contract version this Flyback offers.</summary>
    public static Version Contract { get; } = typeof(Flyback.Plugins.IFlybackPlugin).Assembly.GetName().Version!;

    /// <summary>A package with the picture plugin for Windows and one more entry named <paramref name="name"/>.</summary>
    public static byte[] With(string name, byte[]? bytes = null) =>
        Zip([($"win/{AssemblyName}", Assembly), (name, bytes ?? [0])]);
}
