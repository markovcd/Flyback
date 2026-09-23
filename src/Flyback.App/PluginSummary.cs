using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

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

    /// <summary>What the plugins window says besides the plugins it lists.</summary>
    internal static PluginRun Run(PluginCatalog plugins, string folder, string? soundFailure)
    {
        var problems = plugins.Problems.Select(p => p.ToString()).ToList();

        if (soundFailure is { } failure) problems.Insert(0, $"Could not open sound: {failure}");

        return new PluginRun(folder, problems);
    }

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
