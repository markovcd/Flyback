using System.CommandLine;
using System.Diagnostics;
using Flyback.Core;

namespace Flyback.Cli;

/// <summary>
/// <c>flyback-cli viewer</c>: the viewer, reached from where a person or an agent looks
/// to find out what Flyback can do.
/// </summary>
/// <remarks>
/// A passthrough, not a second declaration of the viewer's flags. Everything after the
/// word goes to <c>flyback-viewer</c> untouched and its exit code comes back, so
/// <c>flyback-cli viewer --help</c> is the viewer printing its own help, a flag added
/// there is here the moment it exists, and this program keeps carrying no UI framework.
/// </remarks>
internal static class ViewerCommand
{
    public const string Name = "viewer";

    /// <summary>Where the viewer is expected: beside this program, in the folder both publish into.</summary>
    public static Func<string> Beside { get; set; } = () =>
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "flyback-viewer.exe" : "flyback-viewer");

    /// <summary>Whether a command line is asking for the viewer.</summary>
    /// <remarks>
    /// Decided before any parsing, because the parser would answer <c>--help</c> itself
    /// and the point is that the viewer does.
    /// </remarks>
    public static bool Claims(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Name, StringComparison.Ordinal);

    /// <summary>
    /// Registered so the command is listed however the machine was laid out: "it ships
    /// with the app" is a better answer than the command not existing.
    /// </summary>
    public static Command Build()
    {
        var rest = new Argument<string[]>("arguments")
        {
            Description = "Handed to flyback-viewer as they are; --help asks it what it takes.",
            Arity = ArgumentArity.ZeroOrMore,
        };

        var command = new Command(Name, "Play a patch: picture and sound, no editor.") { rest };

        command.TreatUnmatchedTokensAsErrors = false;
        command.SetAction(result => Run([.. result.GetValue(rest) ?? [], .. result.UnmatchedTokens], Console.Error));

        return command;
    }

    /// <summary>Starts the viewer with <paramref name="arguments"/> and waits, returning what it returned.</summary>
    public static int Run(IReadOnlyList<string> arguments, TextWriter error)
    {
        var path = Beside();

        if (!File.Exists(path))
        {
            error.WriteLine(
                $"{GlobalConstants.ApplicationName}: the viewer is not here — looked for {path}. It ships beside this program.");

            return Exit.Failed;
        }

        var start = new ProcessStartInfo(path) { UseShellExecute = false };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        try
        {
            using var viewer = Process.Start(start);

            if (viewer is null)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: {path} would not start.");

                return Exit.Failed;
            }

            viewer.WaitForExit();

            return viewer.ExitCode;
        }
        catch (Exception ex)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {path} would not start: {ex.Message}");

            return Exit.Failed;
        }
    }
}
