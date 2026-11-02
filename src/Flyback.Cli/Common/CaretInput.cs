using System.CommandLine;
using System.Globalization;
using Flyback.Core;
using Flyback.Engine.Language;

namespace Flyback.Cli.Common;

/// <summary>A text patch and a place in it, as the commands that answer about a caret take them.</summary>
internal sealed class CaretInput
{
    public Argument<string> Patch { get; } = new("patch")
    {
        Description = "The patch, written as text (." + PatchLanguage.FileExtension + "), or - to read it from standard input.",
    };

    public Option<string> At { get; } = new("--at")
    {
        Description = "Where the caret is, as line:column, both counting from one.",
        Required = true,
    };

    /// <summary>The text and the place, or false having said on <paramref name="error"/> why there are none.</summary>
    public bool Read(ParseResult result, string command, TextWriter error, out string source, out int line, out int column)
    {
        source = string.Empty;
        line = column = 0;

        var at = result.GetValue(At)!.Split(':');

        if (at is not [var l, var c]
            || !int.TryParse(l, NumberStyles.None, CultureInfo.InvariantCulture, out line) || line < 1
            || !int.TryParse(c, NumberStyles.None, CultureInfo.InvariantCulture, out column) || column < 1)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName} {command}: --at takes line:column, both whole numbers from one, like 3:14.");
            return false;
        }

        var path = result.GetValue(Patch)!;

        if (path == "-")
        {
            source = Console.In.ReadToEnd();
            return true;
        }

        if (!File.Exists(path))
        {
            error.WriteLine($"{GlobalConstants.ApplicationName} {command}: {Path.GetFileName(path)} does not exist.");
            return false;
        }

        source = File.ReadAllText(path);
        return true;
    }
}
