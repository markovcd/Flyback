using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Flyback.Plugins.Programs;

/// <summary>
/// A program run once, sent its input on standard input and read to the end.
/// </summary>
/// <remarks>
/// The input goes on standard input because a conversation outgrows a command
/// line. Variables named in <c>without</c> are taken out of the environment it is
/// given: a key there would be billed instead of the plan.
/// </remarks>
internal static class ProgramProcess
{
    /// <summary>The longest one question may take, a backstop for a program that hangs.</summary>
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(15);

    /// <summary>A folder with nothing in it, so no project's instructions are found by looking around.</summary>
    public static string QuietFolder(string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), name);

        Directory.CreateDirectory(folder);

        return folder;
    }

    /// <summary>How the program is started: all three streams redirected, and no console window of its own.</summary>
    internal static ProcessStartInfo StartInfo(
        string executable,
        IReadOnlyList<string> arguments,
        string folder,
        IEnumerable<string> without)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = folder,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var variable in without) start.Environment.Remove(variable);

        return start;
    }

    /// <param name="name">What the person calls the program, for what is said when it fails.</param>
    /// <param name="executable">The program to start.</param>
    /// <param name="arguments">Its command line.</param>
    /// <param name="input">All of standard input.</param>
    /// <param name="folder">Where it runs.</param>
    /// <param name="without">Environment variables it is not given.</param>
    /// <param name="cancel">Stops the program.</param>
    /// <exception cref="ProgramFailure">It would not start, or took too long.</exception>
    public static async Task<(string Output, string Errors, int ExitCode)> Run(
        string name,
        string executable,
        IReadOnlyList<string> arguments,
        string input,
        string folder,
        IEnumerable<string> without,
        CancellationToken cancel)
    {
        var start = StartInfo(executable, arguments, folder, without);

        using var process = new Process { StartInfo = start };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            throw new ProgramFailure($"{name} would not start: {ex.Message}");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(Longest);

        await using var stopper = timeout.Token.Register(() => Stop(process));

        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // It exited before reading; what it said is in the output.
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

        cancel.ThrowIfCancellationRequested();

        if (timeout.IsCancellationRequested)
            throw new ProgramFailure($"{name} took longer than {Longest.TotalMinutes:0} minutes and was stopped.");

        return (await output.ConfigureAwait(false), await errors.ConfigureAwait(false), process.ExitCode);
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Gone already.
        }
    }
}
