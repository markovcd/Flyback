using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Flyback.Core;
using Flyback.Plugins.Hosting;

namespace Flyback.Cli;

/// <summary>What one run of the SDK came to: its exit code and everything it printed.</summary>
internal sealed record Published(int Code, string Output);

/// <summary>
/// Makes a <c>.fbkp</c> out of a plugin project or the folder it was built into, signs
/// it with the author's key, and checks it the way the editor will before writing it
/// (ADR-0132).
/// </summary>
/// <remarks>
/// Nothing is asked that the build already says. A project is published once for each
/// runtime it names, or once portably where it names none. A folder is read the way
/// the SDK lays one out: <c>publish/</c> or the folder itself is the portable build,
/// and each <c>&lt;rid&gt;/publish/</c> or <c>&lt;rid&gt;/</c> is that runtime's. The
/// package carries nothing but the builds: the editor reads what the plugin is from
/// the plugin itself, so what this prints is what the install dialog will show.
/// <para>
/// A build is held to the project file the plugin guide asks for: built with
/// <c>EnableDynamicLoading</c>, and compiled against the host's assemblies without
/// carrying a copy of them.
/// </para>
/// </remarks>
internal static class PackPluginCommand
{
    private const string PublishFolder = "publish";

    /// <param name="source">A project file, a folder holding one, or a folder the SDK built a plugin into.</param>
    /// <param name="dotnet">Runs the SDK with the arguments given. The real <c>dotnet</c> unless a test says otherwise.</param>
    /// <param name="key">The author's private key, as <c>plugin-key</c> writes it.</param>
    /// <param name="checkKeys">Whether a package must be signed; <see cref="PackageSigner.Checked"/> unless a test says otherwise.</param>
    public static int Run(
        FileSystemInfo source,
        FileInfo output,
        TextWriter writer,
        TextWriter error,
        Func<IReadOnlyList<string>, Published>? dotnet = null,
        FileInfo? key = null,
        bool? checkKeys = null)
    {
        dotnet ??= Dotnet;

        ECDsa? signing = null;

        if (key is null)
        {
            if (checkKeys ?? PackageSigner.Checked)
                return Fail(error, "A package must be signed. Make a key once with plugin-key, keep it, and pass it with --key.");
        }
        else
        {
            try
            {
                signing = PackageSigner.Load(File.ReadAllText(key.FullName));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                return Fail(error, $"{key.Name}: {ex.Message}");
            }
        }

        using var signs = signing;

        var building = Path.Combine(Path.GetTempPath(), $"flyback-pack-{Guid.NewGuid():N}");

        try
        {
            if (Project(source) is { } project)
                return Built(project, building, dotnet, error) is { } built ? Pack(built, new HashSet<string>(), signing, output, writer, error, building) : Exit.Failed;

            if (source is not DirectoryInfo { Exists: true } folder)
                return Fail(error, $"{source.Name}: there is no such project or folder.");

            return Found(folder.FullName, error) is var (builds, leave) ? Pack(builds, leave, signing, output, writer, error, building) : Exit.Failed;
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

    /// <summary>Which system a runtime identifier is for, or null for one a package has no place for.</summary>
    internal static string? PlatformOf(string runtime) =>
        PluginPackage.Platforms.FirstOrDefault(p => runtime.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>The project a source names: the file itself, or the one project in a folder.</summary>
    private static FileInfo? Project(FileSystemInfo source) => source switch
    {
        FileInfo { Exists: true } file => file,
        DirectoryInfo { Exists: true } folder when folder.GetFiles("*.csproj") is [var only] => only,
        _ => null,
    };

    /// <summary>Publishes a project once for each runtime it names, or once portably.</summary>
    private static List<(string Platform, string Folder)>? Built(
        FileInfo project,
        string building,
        Func<IReadOnlyList<string>, Published> dotnet,
        TextWriter error)
    {
        var asked = dotnet(["msbuild", project.FullName, "-nologo", "-getProperty:RuntimeIdentifiers", "-getProperty:RuntimeIdentifier"]);

        if (asked.Code != 0)
        {
            error.WriteLine(asked.Output.TrimEnd());
            Fail(error, $"{project.Name} could not be read.");
            return null;
        }

        var runtimes = Runtimes(asked.Output);
        var builds = new List<(string Platform, string Folder)>();

        // A project that names no runtime is built once, portably.
        IEnumerable<string?> each = runtimes.Count > 0 ? runtimes : new string?[] { null };

        foreach (var runtime in each)
        {
            var platform = runtime is null ? PluginPackage.AnyPlatform : PlatformOf(runtime);

            if (platform is null)
            {
                Fail(error, $"{runtime} is not for Windows, macOS or Linux, so a package has no place for it.");
                return null;
            }

            if (Twice(builds, platform, runtime!, error)) return null;

            var into = Path.Combine(building, runtime ?? PluginPackage.AnyPlatform);
            string[] arguments = runtime is null
                ? ["publish", project.FullName, "-o", into, "-nologo"]
                : ["publish", project.FullName, "-r", runtime, "-o", into, "-nologo"];

            var result = dotnet(arguments);

            if (result.Code != 0)
            {
                error.WriteLine(result.Output.TrimEnd());
                Fail(error, $"{project.Name} did not build{(runtime is null ? "" : $" for {runtime}")}.");
                return null;
            }

            builds.Add((platform, into));
        }

        return builds;
    }

    private static readonly string[] RuntimeProperties = ["RuntimeIdentifiers", "RuntimeIdentifier"];

    /// <summary>The runtimes <c>-getProperty</c> reported, from the plural list and the singular alike.</summary>
    private static List<string> Runtimes(string reported)
    {
        using var document = JsonDocument.Parse(reported[reported.IndexOf('{')..]);

        var properties = document.RootElement.GetProperty("Properties");

        return [.. RuntimeProperties
            .SelectMany(name => properties.TryGetProperty(name, out var value) ? (value.GetString() ?? "").Split(';') : [])
            .Select(runtime => runtime.Trim())
            .Where(runtime => runtime.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The builds in a folder the SDK built into, and the folders at its top that are
    /// other builds rather than part of the portable one.
    /// </summary>
    private static (List<(string Platform, string Folder)> Builds, HashSet<string> Leave)? Found(string root, TextWriter error)
    {
        var builds = new List<(string Platform, string Folder)>();
        var leave = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PublishFolder };

        if (First(Path.Combine(root, PublishFolder), root) is { } portable) builds.Add((PluginPackage.AnyPlatform, portable));

        foreach (var folder in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            var runtime = Path.GetFileName(folder);

            if (PlatformOf(runtime) is not { } platform) continue;

            leave.Add(runtime);

            if (First(Path.Combine(folder, PublishFolder), folder) is not { } build) continue;

            if (Twice(builds, platform, runtime, error)) return null;

            builds.Add((platform, build));
        }

        if (builds.Count > 0) return (builds, leave);

        Fail(error, (Missing(Path.Combine(root, PublishFolder)) ?? Missing(root)) is { } why
            ? why
            : $"{Path.GetFileName(root)} holds no plugin build. Point it at the folder the SDK built into, such as bin/Release/net10.0, or at the project.");
        return null;
    }

    /// <summary>
    /// Why the assemblies at the top of a folder hold no plugin, or null where there
    /// are none to ask: none compiled against <c>Flyback.Plugins</c>, or one that was and
    /// has no class implementing <c>IFlybackPlugin</c>.
    /// </summary>
    internal static string? Missing(string folder)
    {
        if (!Directory.Exists(folder)) return null;

        var assemblies = Directory.EnumerateFiles(folder, "*.dll")
            .Order(StringComparer.Ordinal)
            .Select(dll =>
            {
                using var stream = File.OpenRead(dll);
                return (File: Path.GetFileName(dll), Facts: AssemblyFacts.Of(stream));
            })
            .Where(a => a.Facts is not null && !PluginLoadContext.IsHostOwned(a.Facts.Name))
            .ToList();

        if (assemblies.FirstOrDefault(a => a.Facts!.ReferencesContract && !a.Facts.IsPlugin) is { File: { } unfinished })
            return $"{unfinished} references {AssemblyFacts.Contract} but no public class in it implements IFlybackPlugin.";

        if (assemblies.Count > 0 && assemblies.All(a => !a.Facts!.ReferencesContract))
            return $"{string.Join(", ", assemblies.Select(a => a.File))}: none of them references {AssemblyFacts.Contract}, "
                + "so none has an IFlybackPlugin to load. Reference it as the plugin guide's project file does.";

        return null;
    }

    /// <summary>The first of the folders with a plugin assembly at its top, or null for none.</summary>
    private static string? First(params string[] folders) => folders.FirstOrDefault(folder =>
        Directory.Exists(folder)
        && Directory.EnumerateFiles(folder, "*.dll").Any(dll =>
        {
            using var stream = File.OpenRead(dll);
            return AssemblyFacts.Of(stream) is { IsPlugin: true } facts && !PluginLoadContext.IsHostOwned(facts.Name);
        }));

    /// <summary>
    /// Why a build is not what the plugin guide's project file makes, or null where it is.
    /// </summary>
    /// <remarks>
    /// Both are read off the build. Only <c>EnableDynamicLoading</c> gives a library a
    /// <c>runtimeconfig.json</c>, and without it the SDK leaves the plugin's own
    /// dependencies behind. A host assembly in the build is a reference that was copied,
    /// or one left unnamed and brought in by the other: the host loads its own copy
    /// either way, so it is a sign of the project, not a danger in the package.
    /// </remarks>
    internal static string? Unfit(string build, IReadOnlySet<string> leave)
    {
        var plugin = Directory.EnumerateFiles(build, "*.dll").FirstOrDefault(dll =>
        {
            using var stream = File.OpenRead(dll);
            return AssemblyFacts.Of(stream) is { IsPlugin: true } facts && !PluginLoadContext.IsHostOwned(facts.Name);
        });

        if (plugin is not null && !File.Exists(Path.ChangeExtension(plugin, ".runtimeconfig.json")))
            return $"{Path.GetFileName(plugin)} was not built to be loaded as a plugin. "
                + "Set <EnableDynamicLoading>true</EnableDynamicLoading> in its project.";

        var copied = Directory.EnumerateFiles(build, "*.dll", SearchOption.AllDirectories)
            .Where(dll => !leave.Contains(Path.GetRelativePath(build, dll).Split(Path.DirectorySeparatorChar)[0]))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(PluginLoadContext.IsHostOwned)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (copied.Count > 0)
            return $"The build carries {string.Join(" and ", copied.Select(c => $"{c}.dll"))}, which Flyback supplies itself. "
                + "Reference both Flyback.Core and Flyback.Plugins with Private=\"false\" and ExcludeAssets=\"runtime\".";

        return null;
    }

    /// <summary>Whether a second runtime is for a system that already has a build, which a package holds one of.</summary>
    private static bool Twice(List<(string Platform, string Folder)> builds, string platform, string runtime, TextWriter error)
    {
        if (builds.All(b => b.Platform != platform)) return false;

        Fail(error, $"{runtime} is a second build for {PluginPackage.Describe(platform)}, and a package holds one for each system.");
        return true;
    }

    /// <summary>Packs and signs the builds, reads the package back as the editor will, and writes it only if the editor would take it.</summary>
    private static int Pack(
        List<(string Platform, string Folder)> builds,
        IReadOnlySet<string> leave,
        ECDsa? key,
        FileInfo output,
        TextWriter writer,
        TextWriter error,
        string building)
    {
        foreach (var (_, folder) in builds)
        {
            if (Unfit(folder, leave) is { } unfit) return Fail(error, unfit);
        }

        var bytes = PluginPackage.Pack(builds, leave);

        if (key is not null) bytes = PackageSigner.Sign(bytes, key);
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
                return Fail(error, Missing(builds.First(b => b.Platform == platform).Folder)
                    ?? $"The {PluginPackage.Describe(platform)} build has no plugin assembly at its top.");

            if (package.Refusal(platform) is { } refusal)
                return Fail(error, $"{output.Name} would be refused. {refusal}");
        }

        var tried = Tried(builds, leave, building, writer, error);

        if (tried != Exit.Ok) return tried;

        File.WriteAllBytes(output.FullName, bytes);

        Describe(output, package, writer);

        return Exit.Ok;
    }

    /// <summary>
    /// Loads the build this system would install and has it register, failing on
    /// anything Flyback would complain of — above all a module it does not declare.
    /// </summary>
    /// <remarks>
    /// This runs the plugin, which is the author's own code on the author's machine. A
    /// build for another system is not run, and says so. It runs from a copy, so the
    /// author's build folder is never held open.
    /// </remarks>
    private static int Tried(
        List<(string Platform, string Folder)> builds,
        IReadOnlySet<string> leave,
        string building,
        TextWriter writer,
        TextWriter error)
    {
        var here = builds.FirstOrDefault(b => b.Platform == PluginPackage.ThisPlatform);

        if (here.Folder is null) here = builds.FirstOrDefault(b => b.Platform == PluginPackage.AnyPlatform);

        if (here.Folder is null)
        {
            writer.WriteLine($"Not run: there is no build for {PluginPackage.Describe(PluginPackage.ThisPlatform)}, so its modules are checked when it is first loaded.");
            return Exit.Ok;
        }

        var copy = Path.Combine(building, "tried");
        Copy(here.Folder, copy, leave);

        var (problems, loaded) = PluginHost.Try(copy);

        if (problems is [var problem, ..])
            return Fail(error, $"The {PluginPackage.Describe(here.Platform)} build would be refused when loaded. {problem.Source}: {problem.Message}");

        return loaded > 0 ? Exit.Ok : Fail(error, $"The {PluginPackage.Describe(here.Platform)} build loaded no plugin.");
    }

    private static void Copy(string from, string to, IReadOnlySet<string> leave)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);

            if (leave.Contains(relative.Split(Path.DirectorySeparatorChar)[0])) continue;

            var target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    /// <summary>What the install dialog will show, as lines.</summary>
    private static void Describe(FileInfo output, PluginPackage package, TextWriter writer)
    {
        var plugin = package.Description(package.Builds[0]);

        writer.WriteLine(output.Name);
        writer.WriteLine($"  {plugin.Name} {plugin.Version}{(plugin.Author.Length > 0 ? $", by {plugin.Author}" : "")}");

        if (plugin.Description.Length > 0) writer.WriteLine($"  {plugin.Description.Replace("\n", "\n  ")}");

        writer.WriteLine($"  tags      {(plugin.Tags.Count > 0 ? string.Join(", ", plugin.Tags) : "none")}");
        writer.WriteLine($"  preview   {(plugin.Preview is { } preview ? $"{preview.MediaType}, {Math.Max(1, preview.Bytes.Length >> 10)} KB" : "none")}");
        writer.WriteLine($"  adds      {(plugin.Adds.Count > 0 ? string.Join(", ", plugin.Adds) : "nothing Flyback can find")}");

        if (plugin.Modules.Count > 0)
            writer.WriteLine($"  modules   {string.Join(", ", plugin.Modules.Select(m => $"{m.Name} ({m.TypeId})"))}");
        writer.WriteLine($"  reaches   {(plugin.Reaches.Count > 0 ? string.Join(", ", plugin.Reaches) : "nothing outside Flyback that it names")}");
        writer.WriteLine($"  assembly  {plugin.Assembly}.dll");
        writer.WriteLine($"  against   {plugin.BuiltAgainst}");
        writer.WriteLine($"  builds    {string.Join(", ", package.Builds)}");
        writer.WriteLine($"  signed    {(package.Signer is { } signer ? $"key {signer.Fingerprint}" : "no")}");
        writer.WriteLine($"  sha256    {package.Sha256}");
    }

    /// <summary>Runs the SDK's <c>dotnet</c>, capturing what it prints.</summary>
    private static Published Dotnet(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

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
            return new Published(-1, "Building from source needs the .NET SDK's dotnet on PATH. Pack the folder it was built into instead.");
        }
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine($"{GlobalConstants.ApplicationName}: {message}");
        return Exit.Failed;
    }
}
