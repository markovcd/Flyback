using System.Diagnostics;
using Flyback.Core.Tests.Compile;
using Reqnroll.UnitTestProvider;

namespace Flyback.Specs.Support;

/// <summary>A script run under the Node <see cref="NodeJs"/> finds, to its end or a cap.</summary>
internal static class NodeScript
{
    /// <summary>Runs <paramref name="script"/> in <paramref name="folder"/>, and answers how it ended and what it printed and said.</summary>
    /// <param name="what">What the script is, for the skip where there is no Node and the complaint where it runs past <paramref name="cap"/>.</param>
    public static (int Exit, string Printed, string Said) Run(
        IUnitTestRuntimeProvider runtime, string what, string script, IEnumerable<string> arguments, string folder, TimeSpan cap)
    {
        var node = NodeJs.Path;
        Needs.Tool(runtime, node is not null, $"no Node on this machine to run {what} with");

        var start = new ProcessStartInfo(node!, [script, .. arguments])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = folder,
        };

        using var process = Process.Start(start)!;
        var said = process.StandardError.ReadToEndAsync();
        var printed = process.StandardOutput.ReadToEndAsync();

        if (!process.WaitForExit(cap))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException($"{what} ran past {cap.TotalSeconds:0} s and was ended: {said.Result}");
        }

        return (process.ExitCode, printed.Result, said.Result);
    }
}
