namespace Flyback.Cli.Common;

/// <summary>
/// Progress for a long run, and only where somebody is watching. Written to
/// stderr so that it never lands in a redirected file, and carriage returned
/// so that it is one line rather than a thousand.
/// </summary>
internal static class ConsoleProgress
{
    public static IProgress<double>? For(string doing = "rendering")
    {
        if (Console.IsErrorRedirected) return null;

        var last = -1;

        return new Progress<double>(done =>
        {
            var percent = (int)(done * 100);
            if (percent == last) return;

            last = percent;
            Console.Error.Write($"\rrendering… {percent,3}%");

            if (percent >= 100) Console.Error.WriteLine();
        });
    }
}
