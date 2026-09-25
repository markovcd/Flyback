using Flyback.Core;

namespace Flyback.Plugins.Assist;

/// <summary>
/// The modules whose descriptions the assistant is told whatever else is left
/// out — see <see cref="ProsePolicy"/>.
/// </summary>
/// <remarks>
/// A file beside the settings rather than a setting, because it is a list somebody
/// edits by hand and a settings form has no good way to hold a hundred type ids.
/// The copy Flyback ships with is written there once and never again: an edited
/// list is the whole point of there being a file, and a start that put the shipped
/// one back would throw the edits away. Deleting the file brings the shipped list
/// back.
/// </remarks>
internal static class PriorityModules
{
    private const string Resource = "priority-modules.txt";

    public static string File => Path.Combine(GlobalConstants.DataFolder, "priority-modules.txt");

    /// <summary>The list as Flyback ships it, comments and all.</summary>
    public static string Shipped { get; } = ReadShipped();

    /// <summary>
    /// Writes <see cref="Shipped"/> out where there is no file yet. Never throws:
    /// a list that could not be written is read from <see cref="Shipped"/> instead,
    /// which is not worth a failure to start.
    /// </summary>
    /// <param name="path">Somewhere other than the usual place, for the tests.</param>
    public static void Install(string? path = null)
    {
        try
        {
            var to = path ?? File;

            if (System.IO.File.Exists(to)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(to) ?? GlobalConstants.DataFolder);

            // CreateNew rather than a plain write, so a file that turns up between
            // the check and the write is left alone rather than overwritten.
            using var stream = new FileStream(to, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream);

            writer.Write(Shipped);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The type ids in the file, or in <see cref="Shipped"/> where there is no file
    /// or it cannot be read. Never throws.
    /// </summary>
    /// <param name="path">Somewhere other than the usual place, for the tests.</param>
    public static IReadOnlySet<string> Load(string? path = null)
    {
        try
        {
            var from = path ?? File;

            if (System.IO.File.Exists(from)) return Parse(System.IO.File.ReadAllText(from));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Parse(Shipped);
    }

    /// <summary>
    /// One type id a line; blank lines and lines starting with <c>#</c> are passed
    /// over. Ids no module has are kept, since a plugin that is not loaded today
    /// may be tomorrow, and are simply never matched.
    /// </summary>
    public static IReadOnlySet<string> Parse(string text) => text
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .ToHashSet(StringComparer.Ordinal);

    private static string ReadShipped()
    {
        using var stream = typeof(PriorityModules).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"the {Resource} resource is missing from the build.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
