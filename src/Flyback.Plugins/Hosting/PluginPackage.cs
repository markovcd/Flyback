using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Flyback.Plugins.Hosting;

/// <summary>One file a package would put into the plugin's folder.</summary>
/// <param name="Path">Where it goes, relative to the plugin's folder, with forward slashes.</param>
/// <param name="Index">Its place among the zip's entries.</param>
/// <param name="Length">How long the zip says it is.</param>
internal sealed record PackedFile(string Path, int Index, long Length);

/// <summary>How much a package may ask of the disk and the memory reading it.</summary>
/// <param name="Packed">The package itself, which is held in memory whole.</param>
/// <param name="Unpacked">Every file in it together, counted as it is written rather than as the zip says.</param>
internal sealed record PackageLimits(long Packed, long Unpacked, int Entries)
{
    public static PackageLimits Default { get; } = new(128L << 20, 512L << 20, 4096);
}

/// <summary>
/// A <c>.fbkp</c>: a zip with a folder per system the plugin was built for, each
/// holding that system's build (ADR-0132).
/// </summary>
/// <remarks>
/// Nothing in it describes the plugin but the plugin: what it is called and what it
/// can do are read from its assemblies' metadata (<see cref="PluginDescription"/>), so
/// what is shown is what is installed.
/// <para>
/// Read whole into memory and never again from the disk, so what is installed is
/// the package that was shown, not whatever is at its path by the time Install is
/// pressed. A package with one file that would land outside its folder is refused
/// whole rather than installed without it: nobody packs one by accident.
/// </para>
/// </remarks>
internal sealed class PluginPackage
{
    public const string Extension = ".fbkp";

    /// <summary>The folder holding a build for every system, used where there is none for this one.</summary>
    public const string AnyPlatform = "any";

    /// <summary>
    /// Written beside a plugin a package installed, holding the package's hash. A folder
    /// without one was not installed from a package, and no package replaces it.
    /// </summary>
    public const string MarkerName = "package.sha256";

    /// <summary>The folder names a build is looked for under, as runtime identifiers begin.</summary>
    public static IReadOnlyList<string> Platforms { get; } = ["win", "osx", "linux"];

    private readonly byte[] bytes;

    private readonly PackageLimits limits;

    private readonly Dictionary<string, List<PackedFile>> builds;

    private readonly Dictionary<string, PluginDescription> descriptions;

    private PluginPackage(
        byte[] bytes,
        PackageLimits limits,
        Dictionary<string, List<PackedFile>> builds,
        Dictionary<string, PluginDescription> descriptions)
    {
        this.bytes = bytes;
        this.limits = limits;
        this.builds = builds;
        this.descriptions = descriptions;

        Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Builds = [.. Platforms.Append(AnyPlatform).Where(builds.ContainsKey)];
    }

    /// <summary>What the package hashes to, for comparing against what its author publishes.</summary>
    public string Sha256 { get; }

    public long Size => bytes.LongLength;

    /// <summary>The systems it holds a plugin for, <see cref="AnyPlatform"/> last.</summary>
    public IReadOnlyList<string> Builds { get; }

    /// <summary>The system this is running on, as a build's folder is named.</summary>
    public static string ThisPlatform =>
        OperatingSystem.IsWindows() ? "win"
        : OperatingSystem.IsMacOS() ? "osx"
        : OperatingSystem.IsLinux() ? "linux"
        : "";

    public static bool Named(string fileName) => fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    public static string Describe(string platform) => platform switch
    {
        "win" => "Windows",
        "osx" => "macOS",
        "linux" => "Linux",
        AnyPlatform => "any system",
        _ => "this system",
    };

    /// <summary>The build that would be installed on <paramref name="platform"/>, or null for none.</summary>
    public string? BuildFor(string platform) =>
        builds.ContainsKey(platform) ? platform
        : builds.ContainsKey(AnyPlatform) ? AnyPlatform
        : null;

    public IReadOnlyList<PackedFile> Files(string build) => builds[build];

    /// <summary>What a build says it is and what it can do.</summary>
    public PluginDescription Description(string build) => descriptions[build];

    /// <summary>
    /// The plugin as it would be installed on <paramref name="platform"/>, or where
    /// there is no build for it, as the first build describes it — or null for a
    /// package with no build at all.
    /// </summary>
    public PluginDescription? DescriptionFor(string platform) =>
        BuildFor(platform) is { } build ? descriptions[build]
        : Builds.Count > 0 ? descriptions[Builds[0]]
        : null;

    /// <summary>
    /// Why this package cannot be installed on <paramref name="platform"/>, or null
    /// where it can: there is no build for it, or the build was compiled against a
    /// contract this Flyback does not offer.
    /// </summary>
    public string? Refusal(string platform)
    {
        if (BuildFor(platform) is not { } build)
        {
            return Builds.Count == 0
                ? "It holds no plugin for any system."
                : $"It has no build for {Describe(platform)}, only for {string.Join(", ", Builds.Select(Describe))}.";
        }

        return ContractVersion.Reason(descriptions[build].References);
    }

    /// <summary>Reads a package already in memory.</summary>
    /// <exception cref="InvalidDataException">Where it is not a package that may be installed, saying why.</exception>
    public static PluginPackage Read(byte[] bytes, PackageLimits? limits = null)
    {
        limits ??= PackageLimits.Default;

        if (bytes.LongLength > limits.Packed) throw TooLarge(limits.Packed);

        using var zip = Open(bytes);

        if (zip.Entries.Count > limits.Entries)
            throw new InvalidDataException($"It holds more than {limits.Entries} files.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builds = new Dictionary<string, List<PackedFile>>(StringComparer.Ordinal);
        long unpacked = 0;

        for (var index = 0; index < zip.Entries.Count; index++)
        {
            var entry = zip.Entries[index];
            var name = entry.FullName;

            if (Unsafe(name) is { } why)
                throw new InvalidDataException($"It holds a file named \"{PluginDescription.Line(name, 80)}\", {why}.");

            // Case-blind, because two names one file system tells apart are one file on another.
            if (!seen.Add(name.TrimEnd('/')))
                throw new InvalidDataException($"It holds \"{PluginDescription.Line(name, 80)}\" twice.");

            if (name.EndsWith('/')) continue;

            unpacked += entry.Length;

            if (unpacked > limits.Unpacked) throw TooLarge(limits.Unpacked, unpacked: true);

            // Anything outside a build's folder — a readme, a license — stays in the package.
            var slash = name.IndexOf('/');

            if (slash < 0) continue;

            var platform = name[..slash];

            if (platform != AnyPlatform && !Platforms.Contains(platform)) continue;

            if (!builds.TryGetValue(platform, out var files)) builds[platform] = files = [];

            files.Add(new PackedFile(name[(slash + 1)..], index, entry.Length));
        }

        var descriptions = new Dictionary<string, PluginDescription>(StringComparer.Ordinal);

        foreach (var (platform, files) in builds.ToList())
        {
            PluginDescription? description;

            try
            {
                description = PluginDescription.Of(files.Select(f =>
                    (f.Path, (Func<Stream>)(() => new MemoryStream(ReadAll(zip.Entries[f.Index], limits.Unpacked), writable: false)))));
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"{ex.Message} ({platform})");
            }

            // A folder with no plugin assembly at its top is not a build.
            if (description is null) builds.Remove(platform);
            else descriptions[platform] = description;
        }

        return new PluginPackage(bytes, limits, builds, descriptions);
    }

    /// <summary>Reads a package from a stream, never holding more of it than a package may be.</summary>
    public static async Task<PluginPackage> ReadAsync(Stream stream, PackageLimits? limits = null)
    {
        limits ??= PackageLimits.Default;

        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;

        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            if (memory.Length + read > limits.Packed) throw TooLarge(limits.Packed);

            memory.Write(buffer, 0, read);
        }

        return Read(memory.ToArray(), limits);
    }

    /// <summary>
    /// Zips each build folder under its platform's name, leaving out the host's own
    /// assemblies, which the host always supplies itself.
    /// </summary>
    /// <param name="builds">Each platform's name, and the folder holding its build output.</param>
    /// <param name="leave">Folders at the top of a build that are not part of it: the SDK puts other runtimes' builds inside the portable one.</param>
    public static byte[] Pack(IEnumerable<(string Platform, string Folder)> builds, IReadOnlySet<string>? leave = null)
    {
        using var memory = new MemoryStream();

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (platform, folder) in builds)
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    var path = Path.GetRelativePath(folder, file).Replace('\\', '/');

                    if (!path.Contains('/') && PluginLoadContext.IsHostOwned(HostName(path))) continue;

                    if (leave is not null && path.Split('/') is [var top, _, ..] && leave.Contains(top)) continue;

                    zip.CreateEntryFromFile(file, $"{platform}/{path}", CompressionLevel.Optimal);
                }
            }
        }

        return memory.ToArray();
    }

    /// <summary>The assembly a file at a build's top belongs to: <c>Flyback.Core</c> for its dll, pdb and xml alike.</summary>
    private static string HostName(string file) => file.Split('.') switch
    {
        [.. var name, _] => string.Join('.', name),
        _ => file,
    };

    /// <summary>
    /// Writes <paramref name="build"/>'s files into <paramref name="folder"/>, which
    /// must not exist yet.
    /// </summary>
    public void Unpack(string build, string folder)
    {
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        var budget = limits.Unpacked;

        Directory.CreateDirectory(root);

        using var zip = Open(bytes);

        foreach (var file in builds[build])
        {
            // Unsafe already refused every name that could climb out; this is the
            // check that does not depend on having thought of every way to.
            var path = Path.GetFullPath(Path.Combine(root, file.Path));

            if (!path.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidDataException($"\"{file.Path}\" would land outside the plugin's folder.");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var from = zip.Entries[file.Index].Open();
            using var to = new FileStream(path, FileMode.CreateNew, FileAccess.Write);

            budget -= Copy(from, to, budget);
        }
    }

    /// <summary>
    /// Why an entry's name may not be unpacked, or null where it may. Anything that
    /// is a root, a drive, a stream of another file, a way up, or a name Windows
    /// quietly turns into another name.
    /// </summary>
    internal static string? Unsafe(string name)
    {
        if (name.Length == 0) return "which is empty";
        if (name.Length > 240) return "which is too long";
        if (name.StartsWith('/')) return "which starts at the root of the disk";
        if (name.AsSpan().ContainsAny(Forbidden)) return "with a character in it no file name may have";
        if (name.Any(char.IsControl)) return "with a control character in it";

        var parts = name.Split('/');

        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];

            if (part.Length == 0)
            {
                // The one empty part allowed is after a folder's closing slash.
                if (index == parts.Length - 1) continue;
                return "with an empty folder name in it";
            }

            if (part is "." or "..") return "which climbs out of its folder";
            if (part.EndsWith('.') || part.EndsWith(' ')) return "which ends in a dot or a space";
            if (Reserved(part)) return "which Windows keeps for a device";
        }

        return null;
    }

    /// <summary>A backslash is a separator on Windows and a colon names a drive or a stream.</summary>
    private static readonly SearchValues<char> Forbidden = SearchValues.Create("\\:*?\"<>|");

    private static readonly string[] Devices =
        ["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
         .. Enumerable.Range(0, 10).SelectMany(n => new[] { $"COM{n}", $"LPT{n}" })];

    /// <summary>Whether Windows reads <paramref name="part"/> as a device, whatever extension follows it.</summary>
    internal static bool Reserved(string part)
    {
        var stem = part.Split('.')[0].TrimEnd(' ');

        return Devices.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    private static ZipArchive Open(byte[] bytes)
    {
        try
        {
            return new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("It is not a zip file.");
        }
    }

    private static byte[] ReadAll(ZipArchiveEntry entry, long largest)
    {
        using var from = entry.Open();
        using var to = new MemoryStream();

        Copy(from, to, largest);

        return to.ToArray();
    }

    /// <summary>Copies, counting what actually comes out rather than what the zip claimed would.</summary>
    private static long Copy(Stream from, Stream to, long budget)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;

        while ((read = from.Read(buffer)) > 0)
        {
            copied += read;

            if (copied > budget) throw TooLarge(budget, unpacked: true);

            to.Write(buffer, 0, read);
        }

        return copied;
    }

    private static InvalidDataException TooLarge(long limit, bool unpacked = false) =>
        new($"{(unpacked ? "Unpacked, it is" : "It is")} larger than the {limit >> 20} MB a plugin package may be.");
}
