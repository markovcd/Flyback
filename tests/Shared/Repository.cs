namespace Flyback.Tests;

/// <summary>The checkout the tests were built in, for a test that reads its sources, docs or workflows.</summary>
/// <remarks>Linked into each test project that needs it rather than shared through a reference.</remarks>
internal static class Repository
{
    /// <summary>The folder holding <c>Flyback.slnx</c>.</summary>
    public static string Root { get; } = Find();

    /// <summary><paramref name="parts"/> under <see cref="Root"/>.</summary>
    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    private static string Find()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
            if (File.Exists(System.IO.Path.Combine(at.FullName, "Flyback.slnx"))) return at.FullName;

        throw new InvalidOperationException("The tests are not running inside the repository.");
    }
}
