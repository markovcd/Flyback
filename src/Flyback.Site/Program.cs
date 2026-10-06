using System.CommandLine;
using System.Text;
using Flyback.Site.Commands;

namespace Flyback.Site;

/// <summary>
/// The preset site's tool: what its workflows and the author's machine run against it,
/// with the app's own readers, which the Worker cannot run.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        return Run(args, new InvocationConfiguration());
    }

    internal static int Run(string[] args, InvocationConfiguration configuration)
    {
        var root = new RootCommand("flyback-site — checks and loads what the preset site holds.")
        {
            CheckSubmissionCommand.Build(),
            ValidateSubmissionsCommand.Build(),
            PushDefaultsCommand.Build(),
            PushMediaCommand.Build(),
        };

        var parsed = root.Parse(args);
        var code = parsed.Invoke(configuration);

        return parsed.Errors.Count > 0 ? Exit.Failed : code;
    }
}
