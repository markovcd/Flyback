using Flyback.App.Audio;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.App.PluginPackages;

/// <summary>The plugins this run loaded and failed to load, as the plugins window and a sound failure report them.</summary>
internal static class PluginSummary
{
    /// <summary>What loaded and what did not, as the details of a sound failure.</summary>
    internal static string Text(PluginCatalog plugins, string? soundFailure, string? assistantSummary)
    {
        var lines = new List<string> { plugins.Plugins.Count == 0 ? "No plugins loaded." : "Loaded:" };

        lines.AddRange(plugins.Plugins.Select(p => $"    {p.Info.Name}  ({p.Info.Id})"));

        // Module providers are worth naming separately: they are what a saved
        // patch records, and what another machine would have to install.
        var providers = plugins.Modules.Providers.Where(p => p.Id != NodeCatalog.BuiltInProvider.Id).ToList();

        if (providers.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Modules from:");
            lines.AddRange(providers.Select(p => $"    {p.Name}  ({p.Id})"));
        }

        if (soundFailure is { } failure)
            lines.Add($"Could not open sound: {failure}");

        if (plugins.Assistants.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(Assistant(plugins, assistantSummary));
        }

        lines.AddRange(plugins.Problems.Select(p => $"Problem: {p}"));

        lines.Add(string.Empty);
        lines.Add(PluginHost.DefaultDirectory);

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// What went wrong this run: by the plugin folder it happened in, for the plugins
    /// window to mark that plugin's row with, and the rest for it to list on their own.
    /// </summary>
    internal static (PluginRun Run, IReadOnlyDictionary<string, string> Troubles) Run(PluginCatalog plugins, string folder, AudioSetup sound)
    {
        var troubles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var rest = new List<string>();

        void Blame(string? at, string line)
        {
            if (at is null) rest.Add(line);
            else if (troubles.TryGetValue(Folder(at), out var lines)) lines.Add(line);
            else troubles[Folder(at)] = [line];
        }

        if (sound.Failure is { } failure) Blame(FolderOf(plugins, sound.Output), $"Could not open sound: {failure}");

        foreach (var problem in plugins.Problems) Blame(problem.Folder, problem.ToString());

        return (new PluginRun(folder, rest), troubles.ToDictionary(t => t.Key, t => string.Join(Environment.NewLine, t.Value), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>A folder as <see cref="Run"/> keys it.</summary>
    internal static string Folder(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>The folder of the plugin that registered <paramref name="registered"/>, or null where none did.</summary>
    private static string? FolderOf(PluginCatalog plugins, object? registered) =>
        registered is not null && plugins.Provider(registered) is { } info
        && plugins.Plugins.FirstOrDefault(p => p.Info.Id == info.Id) is { } loaded
            ? Path.GetDirectoryName(loaded.AssemblyPath)
            : null;

    /// <summary>
    /// Which third party a patch and its pictures would go to (ADR-0033's disclosure),
    /// and where a key would be kept, but never what it is.
    /// </summary>
    /// <remarks>
    /// An assistant with no store behind it still works; it just forgets between runs,
    /// and saying so is the difference between that and appearing to have saved one.
    /// </remarks>
    internal static string[] Assistant(PluginCatalog plugins, string? assistantSummary) =>
    [
        assistantSummary ?? "No assistant is chosen, so nothing is sent anywhere.",
        plugins.PreferredSecretStore is { } store
            ? $"Keys are kept by {store.Name}."
            : "No secret store is installed, so a key lasts only as long as the window.",
    ];
}
