using System.CommandLine;

namespace Flyback.Specs.Steps;

/// <summary>
/// Runs flyback-cli in this process, one run at a time: a run installs its plugins' modules
/// as the process's catalog, as a real CLI does once, so two at once would read each other's.
/// </summary>
internal static class InProcessCli
{
    private static readonly Lock Gate = new();

    public static int Run(string[] args, Flyback.Cli.Plugins plugins, InvocationConfiguration configuration)
    {
        lock (Gate) return Flyback.Cli.Program.Run(args, plugins, configuration);
    }
}
