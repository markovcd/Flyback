using System.ComponentModel;
using System.Diagnostics;
using Flyback.Core;
using Flyback.Plugins.Hosting;

namespace Flyback.Cli;

/// <summary>What building a project for one system came to: the SDK's exit code and everything it printed.</summary>
internal sealed record Published(int Code, string Output);

/// <summary>
/// Makes a <c>.fbkp</c> out of a plugin project or a folder it was built into, and
/// checks it the way the editor will before writing it (ADR-0132).
/// </summary>
/// <remarks>
/// A project is published once per system with the SDK on PATH; folders already built
/// need no SDK at all. The package carries nothing but the builds: the editor reads
/// what the plugin is from the plugin itself, so what this prints is what the install
/// dialog will show.
/// </remarks>
internal static class PackPluginCommand
{
    /// <summary>The runtime each system's build is published for, as the releases are.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Runtimes = new Dictionary<string, string>
    {
        ["win"] = "win-x64",
        ["osx"] = "osx-arm64",
        ["linux"] = "linux-x64",
    };

    /// <param name="source">A project file to build, or a folder already built, or null where <paramref name="folders"/> name the builds.</param>
    /// <param name="platforms">Which builds the package carries, <see cref="PluginPackage.AnyPlatform"/> where none is named.</param>
    /// <param name="folders">A folder already built for each system named, in place of a source.</param>
    /// <param name="publish">Builds a project for one system into a folder. The SDK's own <c>dotnet publish</c> unless a test says otherwise.</param>
    public static int Run(
        FileSystemInfo? source,
        FileInfo output,
        IReadOnlyList<string> platforms,
        TextWriter writer,
        TextWriter error,
        IReadOnlyDictionary<string, DirectoryInfo>? folders = null,
        Func<string, string, string, Published>? publish = null)
    {
        folders ??= new Dictionary<string, DirectoryInfo>();

        if (source is not null == folders.Count > 0)
            return Fail(error, "Name a project or a build folder, or give each system's folder with --win, --osx, --linux or --any, but not both.");

        if (folders.Count > 0 && platforms.Count > 0)
            return Fail(error, "--platform names the builds of a project or a folder; with --win and the rest, the folders are the builds.");

        if (folders.Values.FirstOrDefault(f => !f.Exists) is { } missing)
            return Fail(error, $"{missing.FullName}: there is no such folder.");

        if (platforms.Count == 0) platforms = [PluginPackage.AnyPlatform];

        if (platforms.FirstOrDefault(p => p != PluginPackage.AnyPlatform && !PluginPackage.Platforms.Contains(p)) is { } unknown)
            return Fail(error, $"{unknown} is not a system a package holds a build for: any, win, osx or linux.");

        var building = Path.Combine(Path.GetTempPath(), $"flyback-pack-{Guid.NewGuid():N}");

        try
        {
            IReadOnlyList<(string Platform, string Folder)> builds;

            if (folders.Count > 0)
            {
                builds = [.. folders.Select(f => (f.Key, f.Value.FullName))];
            }
            else if (source is DirectoryInfo { Exists: true } folder)
            {
                if (platforms.Count > 1)
                    return Fail(error, "A folder is one build. Name the one system it is for, or pack the project instead.");

                builds = [(platforms[0], folder.FullName)];
            }
            else if (source is FileInfo { Exists: true } project)
            {
                var built = new List<(string, string)>();

                foreach (var platform in platforms.Distinct())
                {
                    var into = Path.Combine(building, platform);
                    var result = (publish ?? Publish)(project.FullName, platform, into);

                    if (result.Code != 0)
                    {
                        error.WriteLine(result.Output.TrimEnd());
                        return Fail(error, $"{project.Name} did not build for {PluginPackage.Describe(platform)}.");
                    }

                    built.Add((platform, into));
                }

                builds = built;
            }
            else
            {
                return Fail(error, $"{source!.Name}: there is no such project or folder.");
            }

            var bytes = PluginPackage.Pack(builds);
            PluginPackage package;

            try
            {
                package = PluginPackage.Read(bytes);
            }
            catch (InvalidDataException ex)
            {
                return Fail(error, $"{output.Name} would be refused. {ex.Message}");
            }

            foreach (var (platform, _) in builds)
            {
                if (package.BuildFor(platform) != platform)
                    return Fail(error, $"The {PluginPackage.Describe(platform)} build has no plugin assembly at its top.");

                if (package.Refusal(platform) is { } refusal)
                    return Fail(error, $"{output.Name} would be refused. {refusal}");
            }

            File.WriteAllBytes(output.FullName, bytes);

            Describe(output, package, writer);

            return Exit.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(error, $"{output.Name}: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(building)) Directory.Delete(building, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A temporary folder the system will clear.
            }
        }
    }

    /// <summary>What the install dialog will show, as lines.</summary>
    private static void Describe(FileInfo output, PluginPackage package, TextWriter writer)
    {
        var plugin = package.Description(package.Builds[0]);

        writer.WriteLine(output.Name);
        writer.WriteLine($"  {plugin.Name} {plugin.Version}{(plugin.Author.Length > 0 ? $", by {plugin.Author}" : "")}");

        if (plugin.Description.Length > 0) writer.WriteLine($"  {plugin.Description.Replace("\n", "\n  ")}");

        writer.WriteLine($"  adds      {(plugin.Adds.Count > 0 ? string.Join(", ", plugin.Adds) : "nothing Flyback can find")}");
        writer.WriteLine($"  reaches   {(plugin.Reaches.Count > 0 ? string.Join(", ", plugin.Reaches) : "nothing outside Flyback that it names")}");
        writer.WriteLine($"  assembly  {plugin.Assembly}.dll");
        writer.WriteLine($"  builds    {string.Join(", ", package.Builds)}");
        writer.WriteLine($"  sha256    {package.Sha256}");
    }

    /// <summary>
    /// <c>dotnet publish</c> of a project for one system: portable for
    /// <see cref="PluginPackage.AnyPlatform"/>, and for the others framework-dependent,
    /// since the host brings the runtime.
    /// </summary>
    private static Published Publish(string project, string platform, string into)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "publish", project, "-c", "Release", "-o", into, "--nologo" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (Runtimes.TryGetValue(platform, out var runtime))
        {
            foreach (var argument in new[] { "-r", runtime, "--self-contained", "false" }) start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start)!;

            var errors = process.StandardError.ReadToEndAsync();
            var printed = process.StandardOutput.ReadToEnd();

            process.WaitForExit();

            return new Published(process.ExitCode, printed + errors.Result);
        }
        catch (Win32Exception)
        {
            return new Published(-1, "Building from source needs the .NET SDK's dotnet on PATH. Pack a folder it was built into instead.");
        }
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine($"{GlobalConstants.ApplicationName}: {message}");
        return Exit.Failed;
    }
}
