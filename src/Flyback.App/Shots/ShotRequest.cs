using System.Globalization;

namespace Flyback.App.Shots;

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
internal sealed record ShotRequest(
    string Out, string? Patch, string? Preset, double At, int Width, int Height, string? Select, bool Canvas = false, bool Crop = false)
{
    public const string Flag = "--shot";

    /// <summary>Whether a command line asks for a shot rather than a window.</summary>
    public static bool Claims(string[] args) => args.Length > 0 && args[0] == Flag;

    /// <summary>
    /// Reads <c>--shot OUT --at SECONDS --size WxH [--select NAME] [--canvas] [--crop] (--preset NAME | PATCH)</c>,
    /// or says on <paramref name="error"/> what is wrong with it.
    /// </summary>
    public static ShotRequest? Parse(string[] args, TextWriter error)
    {
        if (!Claims(args) || args.Length < 2) return Refuse("give the PNG to write after --shot.");

        var output = args[1];
        string? patch = null, preset = null, select = null;
        bool canvas = false, crop = false;
        var at = 0d;
        var (width, height) = (0, 0);

        for (var i = 2; i < args.Length; i++)
        {
            var flag = args[i];

            if (!flag.StartsWith("--", StringComparison.Ordinal))
            {
                patch = flag;
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
                case "--at" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0:
                    at = seconds;
                    break;
                case "--size" when Size(value) is { } size:
                    (width, height) = size;
                    break;
                case "--select":
                    select = value;
                    break;
                case "--preset":
                    preset = value;
                    break;
                default:
                    return Refuse($"{flag} {value} is not something a shot takes.");
            }
        }

        if ((patch is null) == (preset is null)) return Refuse("give a patch or --preset, and not both.");
        if (width == 0) return Refuse("give --size as WIDTHxHEIGHT.");

        return new ShotRequest(output, patch, preset, at, width, height, select, canvas, crop);

        ShotRequest? Refuse(string why)
        {
            error.WriteLine($"Flyback {Flag}: {why}");
            return null;
        }
    }

    private static (int Width, int Height)? Size(string text)
    {
        var parts = text.Split('x', 'X');

        return parts.Length == 2
            && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var width)
            && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var height)
            && width > 0 && height > 0
                ? (width, height)
                : null;
    }
}
