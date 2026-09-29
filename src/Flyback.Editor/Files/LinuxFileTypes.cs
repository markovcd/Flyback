using System.Diagnostics;
using System.Text;

namespace Flyback.App.Files;

/// <summary>
/// A desktop entry, a MIME package and an icon per kind in the user's own data
/// folder, which a file manager reads to know Flyback's files and what opens them.
/// </summary>
/// <param name="data">The XDG data folder, <c>~/.local/share</c> unless the environment says otherwise.</param>
/// <param name="refresh">Rebuilds a desktop cache from a folder under <paramref name="data"/>.</param>
internal sealed class LinuxFileTypes(string data, string editor, string viewer, Action<string, string> refresh) : FileTypes
{
    internal const string Id = "io.github.markovcd.flyback";

    public static LinuxFileTypes ForUser(string editor, string viewer) => new(DataFolder(), editor, viewer, Run);

    internal string Entry => Path.Combine(data, "applications", $"{Id}.desktop");

    internal string Package => Path.Combine(data, "mime", "packages", $"{Id}.xml");

    /// <summary>Where the icon theme looks for a MIME type's icon, by the name it derives from the type.</summary>
    internal string IconOf(FileKind kind) =>
        Path.Combine(data, "icons", "hicolor", "256x256", "mimetypes", $"{kind.MimeType.Replace('/', '-')}.png");

    public override void Apply(FileOpener opener)
    {
        if (Program(opener, editor, viewer) is { } program)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Entry)!);
            Directory.CreateDirectory(Path.GetDirectoryName(Package)!);

            File.WriteAllText(Entry, DesktopEntry(program, opener == FileOpener.Viewer));
            File.WriteAllText(Package, MimePackage());

            foreach (var kind in Kinds)
            {
                if (!File.Exists(Icon(editor, kind, ".png"))) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(IconOf(kind))!);
                File.Copy(Icon(editor, kind, ".png"), IconOf(kind), overwrite: true);
            }
        }
        else
        {
            File.Delete(Entry);
            File.Delete(Package);

            foreach (var kind in Kinds.Where(kind => File.Exists(IconOf(kind)))) File.Delete(IconOf(kind));
        }

        refresh("update-mime-database", Path.Combine(data, "mime"));
        refresh("update-desktop-database", Path.Combine(data, "applications"));
    }

    /// <remarks>
    /// The viewer is kept out of the applications menu: started from there it has no
    /// file, and plays the startup preset.
    /// </remarks>
    internal static string DesktopEntry(string program, bool viewer) =>
        $"""
        [Desktop Entry]
        Type=Application
        Name={(viewer ? "Flyback Viewer" : "Flyback")}
        Comment=Patchable synthesizer: one module graph makes a picture and a sound
        Exec={Quote(program)} %f
        Terminal=false
        NoDisplay={(viewer ? "true" : "false")}
        Categories=AudioVideo;Audio;Video;
        MimeType={string.Concat(Kinds.Select(kind => kind.MimeType + ";"))}

        """;

    internal static string MimePackage() =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
        {string.Concat(Kinds.Select(kind =>
            $"""
              <mime-type type="{kind.MimeType}">
                <comment>{kind.Name}</comment>
                <glob pattern="*{kind.Extension}"/>
              </mime-type>

            """))}</mime-info>

        """;

    /// <summary>
    /// One argument of an <c>Exec</c> line: quoted, with the four characters the
    /// quotes do not protect escaped, then escaped again as a desktop-entry string.
    /// </summary>
    internal static string Quote(string path)
    {
        const char backslash = (char)92;

        var quoted = new StringBuilder("\"");

        foreach (var c in path)
        {
            if (c is '"' or '`' or '$' or backslash) quoted.Append(backslash);
            quoted.Append(c);
        }

        quoted.Append('"');

        return quoted.ToString()
            .Replace(backslash.ToString(), new string(backslash, 2), StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal);
    }

    private static string DataFolder() =>
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } set
            ? set
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    /// <summary>
    /// A desktop without the tool reads the folder directly, only later, so one that
    /// is missing is not a failure.
    /// </summary>
    private static void Run(string tool, string folder)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(tool) { ArgumentList = { folder }, UseShellExecute = false });

            process?.WaitForExit(10_000);
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
