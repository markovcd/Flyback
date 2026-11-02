using Flyback.Core;

namespace Flyback.Ui.Controls;

/// <summary>
/// The color schemes there are: the ones shipped inside Flyback, and the ones in the
/// user's schemes folder, which win on a shared name.
/// </summary>
internal sealed class SchemeLibrary
{
    /// <summary>More files than anybody keeps schemes in; the rest of a folder this full is not read.</summary>
    public const int MostFiles = 200;

    private SchemeLibrary(IReadOnlyList<Scheme> schemes, IReadOnlyList<string> problems) =>
        (Schemes, Problems) = (schemes, problems);

    /// <summary>Where a person puts a scheme of their own, one <c>.json</c> per scheme.</summary>
    public static string UserFolder => Path.Combine(GlobalConstants.DataFolder, "schemes");

    /// <summary>The schemes shipped inside Flyback, the default first.</summary>
    public static IReadOnlyList<Scheme> Shipped { get; } = ReadShipped();

    /// <summary>Every scheme there is, the default first.</summary>
    public IReadOnlyList<Scheme> Schemes { get; }

    /// <summary>What was wrong with the files in the folder, each line naming its file.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>The shipped schemes and those in <paramref name="folder"/>, or the shipped ones alone for none.</summary>
    /// <remarks>
    /// Never throws: a file that will not read is skipped and said in <see cref="Problems"/>,
    /// since a scheme somebody is halfway through writing is no reason for the window not to open.
    /// </remarks>
    public static SchemeLibrary Load(string? folder)
    {
        var schemes = Shipped.ToList();
        var problems = new List<string>();

        foreach (var (file, json) in ReadFolder(folder, problems))
        {
            var said = new List<string>();

            if (SchemeFile.Read(json, Shipped[0], said) is { } scheme)
            {
                var shipped = schemes.FindIndex(existing => Same(existing.Name, scheme.Name));

                if (shipped >= 0) schemes[shipped] = scheme;
                else schemes.Add(scheme);
            }

            problems.AddRange(said.Select(line => $"{file} {line}"));
        }

        return new SchemeLibrary(schemes, problems);
    }

    /// <summary>The scheme so named, or the first there is where none is.</summary>
    public Scheme Find(string? name) => Named(name) ?? Schemes[0];

    /// <summary>The scheme so named, ignoring case, or null.</summary>
    public Scheme? Named(string? name) => Schemes.FirstOrDefault(scheme => Same(scheme.Name, name));

    private static bool Same(string one, string? other) => string.Equals(one, other, StringComparison.OrdinalIgnoreCase);

    private static List<Scheme> ReadShipped()
    {
        var assembly = typeof(SchemeLibrary).Assembly;
        var schemes = new List<Scheme>();

        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith("schemes/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var problems = new List<string>();

            schemes.Add(SchemeFile.Read(reader.ReadToEnd(), fallback: null, problems)
                ?? throw new InvalidOperationException($"The shipped {resource} {string.Join(' ', problems)}"));
        }

        return [.. schemes.OrderBy(scheme => scheme.Name != Scheme.DefaultName).ThenBy(scheme => scheme.Name, StringComparer.Ordinal)];
    }

    private static IEnumerable<(string File, string Json)> ReadFolder(string? folder, List<string> problems)
    {
        if (folder is null || !Directory.Exists(folder)) yield break;

        foreach (var path in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase).Take(MostFiles))
        {
            var file = Path.GetFileName(path);
            string json;

            try
            {
                if (new FileInfo(path).Length > SchemeFile.MostBytes)
                {
                    problems.Add($"{file} is larger than {SchemeFile.MostBytes / 1024} KB, so it is not a color scheme.");
                    continue;
                }

                json = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{file} could not be read: {ex.Message}");
                continue;
            }

            yield return (file, json);
        }
    }
}
