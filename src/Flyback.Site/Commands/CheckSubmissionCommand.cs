using System.CommandLine;
using Flyback.Site.Checking;
using Flyback.Site.Reading;

namespace Flyback.Site.Commands;

/// <summary>Reads one submitted file as the site does, and prints what it says as JSON.</summary>
internal static class CheckSubmissionCommand
{
    public static Command Build()
    {
        var file = new Argument<FileInfo>("file") { Description = "The submitted .fbk, .fbkb or .fbkp." };

        var name = new Option<string?>("--name") { Description = "The name a preset was submitted under, where one was given." };

        var command = new Command(
            "check-submission",
            "Read a submitted preset or plugin package as the preset site does, without running it, and print the verdict as JSON. Exits 1 where it is refused.")
        {
            file, name,
        };

        command.SetAction(result => Run(
            result.GetRequiredValue(file),
            result.GetValue(name),
            BrowserPlugins.Linked(),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error));

        return command;
    }

    internal static int Run(FileInfo file, string? name, BrowserPlugins browser, TextWriter output, TextWriter error)
    {
        if (!file.Exists)
        {
            error.WriteLine($"flyback-site: {file.FullName}: there is no such file.");
            return Exit.Failed;
        }

        var check = Checks.Of(Checks.IsPlugin(file.Name), file.Name, File.ReadAllBytes(file.FullName), name, browser, error);

        output.WriteLine(Checks.ToJson(check));

        return Checks.Accepted(check) ? Exit.Ok : Exit.Refused;
    }
}
