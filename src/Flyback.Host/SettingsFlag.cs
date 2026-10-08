namespace Flyback.Host;

/// <summary>
/// The <c>--settings</c> a command line names, found before the command exists: a
/// program reads its defaults from that file and builds its command from them, so
/// it cannot wait for the parse.
/// </summary>
internal static class SettingsFlag
{
    public static string? PathIn(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--settings") return i + 1 < args.Length ? args[i + 1] : null;

            if (args[i].StartsWith("--settings=", StringComparison.Ordinal)) return args[i]["--settings=".Length..];
        }

        return null;
    }
}
