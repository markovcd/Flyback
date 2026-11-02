using System.Globalization;
using Flyback.Engine.Render;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Desktop.Shots;

/// <summary>What <c>Flyback --shot</c> is asked to draw, in the form <c>flyback-cli shot</c> hands it over.</summary>
/// <param name="Out">The PNG to write.</param>
/// <param name="Patch">The file to open, or null for <paramref name="Preset"/>.</param>
/// <param name="Preset">The shipped preset to open, or null for <paramref name="Patch"/>.</param>
/// <param name="At">The second of the patch the picture is of.</param>
/// <param name="Width">The window's width, in pixels.</param>
/// <param name="Height">The window's height, in pixels.</param>
/// <param name="Select">The box or module to select, by name, or null for nothing.</param>
/// <param name="Canvas">Whether the canvas shows, even for a patch whose text is the document.</param>
/// <param name="Crop">Whether only the canvas around the modules is written.</param>
/// <param name="Measure">Whether the outputs are measured from <paramref name="At"/> and pinned before the picture is taken.</param>
/// <param name="Assistant">Whether the assistant's column is open, showing the conversation the patch carries.</param>
/// <param name="Live">Whether the picture has the window with the patch over it, as live patching shows it.</param>
/// <param name="Open">Whether every box is drawn open.</param>
/// <param name="Shut">Whether every box is drawn shut.</param>
/// <param name="Scheme">The color scheme the window is drawn in.</param>
/// <param name="Gallery">Whether the preset gallery is open over the window.</param>
internal sealed record ShotRequest(
    string Out, string? Patch, string? Preset, double At, int Width, int Height, string? Select, bool Canvas = false, bool Crop = false, bool Measure = false,
    bool Assistant = false, bool Live = false, bool Open = false, bool Shut = false, Scheme? Scheme = null, bool Gallery = false)
{
    public const string Flag = "--shot";

    /// <summary>Whether a command line asks for a shot rather than a window.</summary>
    public static bool Claims(string[] args) => args.Length > 0 && args[0] == Flag;

    /// <summary>
    /// Reads <c>--shot OUT --at SECONDS --size WxH [--select NAME] [--canvas] [--crop] [--measure] [--assistant] [--live] [--open | --shut] [--colors SCHEME] [--gallery] (--preset NAME | PATCH)</c>,
    /// or says on <paramref name="error"/> what is wrong with it.
    /// </summary>
    public static ShotRequest? Parse(string[] args, TextWriter error)
    {
        if (!Claims(args) || args.Length < 2) return Refuse("give the PNG to write after --shot.");

        var output = args[1];
        string? patch = null, preset = null, select = null;
        bool canvas = false, crop = false, measure = false, assistant = false, live = false, open = false, shut = false, gallery = false;
        var at = 0d;
        Scheme? scheme = null;
        var (width, height) = (0, 0);

        for (var i = 2; i < args.Length; i++)
        {
            var flag = args[i];

            if (!flag.StartsWith("--", StringComparison.Ordinal))
            {
                patch = flag;
                continue;
            }

            if (flag == "--measure")
            {
                measure = true;
                continue;
            }

            if (flag == "--assistant")
            {
                assistant = true;
                continue;
            }

            if (flag == "--live")
            {
                live = true;
                continue;
            }

            if (flag == "--open")
            {
                open = true;
                continue;
            }

            if (flag == "--gallery")
            {
                gallery = true;
                continue;
            }

            if (flag == "--shut")
            {
                shut = true;
                continue;
            }

            if (flag == "--canvas" || flag == "--crop")
            {
                canvas = true;
                crop |= flag == "--crop";
                continue;
            }

            if (i + 1 >= args.Length) return Refuse($"{flag} needs a value.");

            var value = args[++i];

            switch (flag)
            {
                case "--at" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                                 && double.IsFinite(seconds) && seconds >= 0:
                    at = seconds;
                    break;
                case "--size" when FrameSize.Of(value) is { } size:
                    (width, height) = size;
                    break;
                case "--select":
                    select = value;
                    break;
                case "--preset":
                    preset = value;
                    break;
                case "--colors" when value.EndsWith(".json", StringComparison.OrdinalIgnoreCase):
                    if (SchemeFrom(value, error) is not { } read) return null;
                    scheme = read;
                    break;
                case "--colors" when SchemeLibrary.Load(null).Named(value) is { } named:
                    scheme = named;
                    break;
                case "--colors":
                    return Refuse($"--colors is a scheme file or one of {string.Join(", ", SchemeLibrary.Shipped.Select(s => s.Name))}, not {value}.");
                default:
                    return Refuse($"{flag} {value} is not something a shot takes.");
            }
        }

        if ((patch is null) == (preset is null)) return Refuse("give a patch or --preset, and not both.");
        if (width == 0) return Refuse("give --size as WIDTHxHEIGHT.");
        if (open && shut) return Refuse("give --open or --shut, and not both.");

        return new ShotRequest(output, patch, preset, at, width, height, select, canvas, crop, measure, assistant, live, open, shut, scheme, gallery);

        ShotRequest? Refuse(string why)
        {
            error.WriteLine($"Flyback {Flag}: {why}");
            return null;
        }
    }

    /// <summary>
    /// The scheme a file holds, read as the editor reads its schemes folder: a color the
    /// file lacks is Phosphor's, and said on <paramref name="error"/>. Null for no scheme at all.
    /// </summary>
    private static Scheme? SchemeFrom(string path, TextWriter error)
    {
        var problems = new List<string>();
        Scheme? scheme = null;

        try
        {
            if (new FileInfo(path).Length > SchemeFile.MostBytes) problems.Add($"is larger than {SchemeFile.MostBytes / 1024} KB.");
            else scheme = SchemeFile.Read(File.ReadAllText(path), Scheme.Phosphor, problems);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"could not be read: {ex.Message}");
        }

        foreach (var problem in problems) error.WriteLine($"Flyback {Flag}: {Path.GetFileName(path)} {problem}");

        return scheme;
    }
}
