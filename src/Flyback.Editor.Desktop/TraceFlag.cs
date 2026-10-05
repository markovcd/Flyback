namespace Flyback.Editor.Desktop;

/// <summary>The <c>--trace &lt;file&gt;</c> a launch is given to write its stalls into.</summary>
internal static class TraceFlag
{
    public const string Name = "--trace";

    /// <summary>
    /// The file <see cref="Name"/> names, or null, and the arguments without both, so the file is
    /// never read as a patch to open.
    /// </summary>
    public static (string? Path, string[] Without) Taken(string[] args)
    {
        var at = Array.FindIndex(args, a => string.Equals(a, Name, StringComparison.OrdinalIgnoreCase));

        return at < 0 || at + 1 >= args.Length
            ? (null, at < 0 ? args : args[..at])
            : (args[at + 1], [.. args[..at], .. args[(at + 2)..]]);
    }
}
