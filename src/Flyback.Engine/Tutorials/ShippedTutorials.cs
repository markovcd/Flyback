namespace Flyback.Engine.Tutorials;

/// <summary>The tutorials that ship with Flyback, in the order a newcomer should take them.</summary>
internal static class ShippedTutorials
{
    /// <summary>Each tutorial's id, which is its file's name under <c>Tutorials/</c>.</summary>
    internal static readonly string[] Order = ["first-patch", "first-sound", "seeing-a-sound", "beat", "play-it", "echo-and-space", "patch-as-text"];

    private const string Prefix = "tutorials/";

    private static readonly Lazy<IReadOnlyList<Tutorial>> Loaded = new(() => [.. Order.Select(Load)]);

    public static IReadOnlyList<Tutorial> All => Loaded.Value;

    /// <summary>A tutorial by its id or its title, ignoring case, or null.</summary>
    public static Tutorial? Find(string name) => All.FirstOrDefault(t =>
        string.Equals(t.Id, name, StringComparison.OrdinalIgnoreCase)
        || string.Equals(t.Title, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The ids of every tutorial file the assembly carries, listed in <see cref="Order"/> or not.</summary>
    internal static IEnumerable<string> Carried => typeof(ShippedTutorials).Assembly
        .GetManifestResourceNames()
        .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
        .Select(n => Path.GetFileNameWithoutExtension(n[Prefix.Length..]));

    private static Tutorial Load(string id)
    {
        using var stream = typeof(ShippedTutorials).Assembly.GetManifestResourceStream($"{Prefix}{id}.md")
            ?? throw new InvalidOperationException($"No tutorial file '{id}.md' is embedded.");
        using var reader = new StreamReader(stream);

        return TutorialReader.Read(id, reader.ReadToEnd());
    }
}
