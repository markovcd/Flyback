using System.Diagnostics;
using Flyback.Tests;
using Reqnroll.UnitTestProvider;

namespace Flyback.Specs.Support;

/// <summary>The website as Hugo builds it from <c>site/</c> (ADR-0222), built once a run.</summary>
internal static class BuiltSite
{
    /// <summary>How long a build may take; one takes well under a second.</summary>
    private static readonly TimeSpan Cap = TimeSpan.FromMinutes(1);

    /// <summary>The Hugo on the path, or null where there is none.</summary>
    public static string? Hugo { get; } = Find();

    private static readonly Lazy<string> Built = new(Build);

    /// <summary>The folder the site was built into.</summary>
    /// <exception cref="InvalidOperationException">There is no Hugo, or the build failed.</exception>
    public static string Root => Built.Value;

    /// <summary>Skips the scenario where there is no Hugo, and fails one not tagged <c>@hugo</c>.</summary>
    public static void Need(IUnitTestRuntimeProvider runtime) =>
        Needs.Tool(runtime, TestCategory.Hugo, Hugo is not null, "no Hugo on this machine to build the website with (scripts/install-hugo.sh)");

    private static string? Find()
    {
        var name = OperatingSystem.IsWindows() ? "hugo.exe" : "hugo";

        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(directory => directory.Length > 0)
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists);
    }

    private static string Build()
    {
        if (Hugo is null) throw new InvalidOperationException("No Hugo on this machine to build the website with (scripts/install-hugo.sh).");

        var folder = Directory.CreateTempSubdirectory("flyback-site").FullName;
        var start = new ProcessStartInfo(Hugo, ["--source", "site", "--destination", folder, "--quiet"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Repository.Root,
        };

        using var process = Process.Start(start)!;
        var said = process.StandardError.ReadToEndAsync();
        var printed = process.StandardOutput.ReadToEndAsync();

        if (!process.WaitForExit(Cap))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException($"hugo ran past {Cap.TotalSeconds:0} s and was ended: {said.Result}");
        }

        if (process.ExitCode != 0) throw new InvalidOperationException($"hugo failed to build the website: {said.Result}{printed.Result}");

        return folder;
    }
}
