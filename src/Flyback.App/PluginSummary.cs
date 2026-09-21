using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>The plugins this run loaded and failed to load, as the About box and a sound failure report them.</summary>
internal static class PluginSummary
{
    /// <summary>
    /// What loaded and what did not. This is the only place a plugin failure is
    /// reported, so it says where the folder is even when it is empty.
    /// </summary>
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

        // Where a key would be kept, but never what it is. An assistant with no
        // store behind it still works; it just forgets between runs, and saying
        // so here is the difference between that and appearing to have saved one.
        if (plugins.Assistants.Count > 0)
        {
            lines.Add(string.Empty);

            // Which third party a patch and its pictures would go to —
            // ADR-0033's disclosure, said here so it does not depend on the
            // assistant panel being open.
            lines.Add(assistantSummary ?? "assistant: none");

            lines.Add(plugins.PreferredSecretStore is { } store
                ? $"Keys are kept by: {store.Name}"
                : "No secret store is installed, so a key lasts only as long as the window.");
        }

        lines.AddRange(plugins.Problems.Select(p => $"Problem: {p}"));

        lines.Add(string.Empty);
        lines.Add(PluginHost.DefaultDirectory);

        return string.Join(Environment.NewLine, lines);
    }
}
