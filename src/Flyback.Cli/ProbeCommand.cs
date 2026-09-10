using System.Text.Json;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;

namespace Flyback.Cli;

/// <summary>
/// What to ask, and where the answer goes.
/// </summary>
/// <param name="Provider">Which assistant, or null for whichever the settings or the catalogue prefer.</param>
/// <param name="Only">Models to ask about by name, or empty for the provider's own shortlist.</param>
/// <param name="Dry">Print what was found and write none of it down.</param>
/// <param name="Keys">
/// Say where each provider's key would come from and ask nothing. The one thing here
/// that costs nothing, which is the point: whether a key is found is the question
/// everything else depends on.
/// </param>
internal sealed record ProbeOptions(
    string? Provider,
    IReadOnlyList<string> Only,
    bool All,
    bool Bounds,
    bool Dry,
    bool Json,
    bool Keys = false);

/// <summary>
/// Asks a provider what its endpoint actually offers, and keeps the answer.
/// </summary>
/// <remarks>
/// Here rather than in the window because it is the shape of a command: it takes
/// minutes, it costs money, and its whole output is a line in a settings file both
/// programs read — the window's model box fills itself in from what this wrote, see
/// <see cref="AssistantSchema.Surveyed"/>. Every reason it cannot run is a sentence
/// rather than a stack trace, because all of them are things somebody can act on.
/// </remarks>
internal static class ProbeCommand
{
    /// <param name="settingsPath">
    /// Somewhere other than the usual place, for the tests. A command that both
    /// reads and writes the real file is one no test can call safely.
    /// </param>
    public static async Task<int> Run(
        PluginCatalog plugins,
        ProbeOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancel,
        string? settingsPath = null)
    {
        var settings = AssistantSettings.Load(settingsPath);

        // Built once and used for both paths, so that what --keys reports is
        // literally what the probe would go on to use rather than a second
        // opinion about it.
        var credentials = new Credentials(plugins.PreferredSecretStore);

        if (options.Keys) return Keys(plugins, credentials, output);

        if (Everyone(plugins, options.Provider))
            return await Each(plugins, settings, credentials, options, output, error, cancel, settingsPath).ConfigureAwait(false);

        // The named one, then the one this machine was left on, then whichever
        // the catalogue would put in front of somebody. The middle is what makes
        // a bare `probe` mean "the one I am using" — deliberately not "every one
        // I have a key for", because every question here is billed and a bare
        // command should not fan out across providers on its own.
        var wanted = options.Provider
            ?? (string.IsNullOrWhiteSpace(settings.Provider) ? null : settings.Provider);

        var assistant = wanted is null ? plugins.PreferredAssistant : plugins.Assistant(wanted);

        if (assistant is null)
        {
            error.WriteLine(wanted is null
                ? "No assistant is installed."
                : $"No assistant called '{wanted}' is installed.");

            if (plugins.Assistants.Count > 0)
                error.WriteLine($"Installed: {string.Join(", ", plugins.Assistants.Select(a => a.Id))}.");

            return Exit.Failed;
        }

        return await One(settings, credentials, assistant, options, output, error, cancel, settingsPath).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether <c>--provider</c> named the whole lot rather than one of them.
    /// </summary>
    /// <remarks>
    /// An installed assistant actually called <c>all</c> wins, because a plugin
    /// that took the name is a real thing somebody can point at and this is only
    /// a word.
    /// </remarks>
    private static bool Everyone(PluginCatalog plugins, string? provider) =>
        string.Equals(provider, "all", StringComparison.OrdinalIgnoreCase)
        && plugins.Assistant("all") is null;

    /// <summary>
    /// Every provider that can be asked and has a key, one after another.
    /// </summary>
    /// <remarks>
    /// One that cannot be asked is a line rather than the end of the run: the point of
    /// asking for all of them is to get whatever is obtainable in one pass. Each is
    /// written down as it finishes, so a later refusal cannot cost an earlier answer.
    /// </remarks>
    private static async Task<int> Each(
        PluginCatalog plugins,
        AssistantSettings settings,
        Credentials credentials,
        ProbeOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancel,
        string? settingsPath)
    {
        if (plugins.Assistants.Count == 0)
        {
            error.WriteLine("No assistant is installed.");

            return Exit.Failed;
        }

        var any = false;
        var first = true;

        foreach (var assistant in plugins.Assistants.OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            if (!first) output.WriteLine();

            first = false;

            var code = await One(settings, credentials, assistant, options, output, error, cancel, settingsPath)
                .ConfigureAwait(false);

            any |= code == Exit.Ok;
        }

        return any ? Exit.Ok : Exit.Failed;
    }

    private static async Task<int> One(
        AssistantSettings settings,
        Credentials credentials,
        IPatchAssistant assistant,
        ProbeOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancel,
        string? settingsPath)
    {
        if (assistant is not IModelSurvey survey)
        {
            error.WriteLine(
                $"{assistant.Name} cannot be asked what it offers, so its models stay whatever "
                + "somebody wrote down.");

            return Exit.Failed;
        }

        // The same account and the same variable the window asks under, through
        // the same class, so the two programs cannot disagree about whether
        // there is a key. What this cannot reach is a key typed into the window
        // and not kept: that one lives in the window's memory and dies with it.
        var variable = assistant.Credential.EnvironmentVariable;

        if (credentials.Of(assistant.Id, variable) is not { } key)
        {
            error.WriteLine($"No key for {assistant.Name}.");

            if (!string.IsNullOrWhiteSpace(variable))
                error.WriteLine($"Set {variable} in this shell, or enter one in the window's Settings.");

            error.WriteLine(credentials.Store is { } store
                ? $"Nothing is kept for '{assistant.Id}' in {store.Name}, which is where the window "
                  + "puts a key it was asked to keep."
                : "Nothing installed here can hold a key, so a key kept by the window is not one "
                  + "this can reach — see `probe --keys`.");

            return Exit.Failed;
        }

        var values = settings.Of(assistant.Id);

        if (!options.Json)
        {
            output.WriteLine($"{assistant.Name}, with a key from {credentials.SourceOf(assistant.Id, variable)}.");
            output.WriteLine();
        }

        IReadOnlyList<ModelReport> found;

        try
        {
            found = await survey.Survey(
                new AssistantConfig(key, values),
                new SurveyOptions(options.Only, options.All, options.Bounds),
                options.Json ? null : new Progress<string>(output.WriteLine),
                cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("Stopped. Nothing was written.");

            return Exit.Failed;
        }
        catch (HttpRequestException e)
        {
            error.WriteLine(e.Message);

            return Exit.Failed;
        }

        if (found.Count == 0)
        {
            error.WriteLine("Nothing answered. Nothing was written, so what was there is still there.");

            return Exit.Problems;
        }

        if (options.Dry)
        {
            Say(found, output, options.Json);
            output.WriteLine();
            output.WriteLine($"{Writing.Count(found.Count, "model")} answered. Asked not to write it down.");

            return Exit.Ok;
        }

        try
        {
            settings.Remember(assistant.Id, values.With(Survey.Key, Survey.Write(found)));
            settings.Save(settingsPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Found {Writing.Count(found.Count, "model")} and could not write them down: {e.Message}");

            return Exit.Failed;
        }

        Say(found, output, options.Json);

        if (!options.Json)
        {
            output.WriteLine();
            output.WriteLine($"{Writing.Count(found.Count, "model")} written to {AssistantSettings.File}.");
        }

        return Exit.Ok;
    }

    /// <summary>
    /// Where every installed provider's key would come from, asking nothing of anybody.
    /// </summary>
    /// <remarks>
    /// Worth a flag of its own because the alternative is to run a survey, and a survey
    /// costs money. It also answers whether anything installed here can hold a key at
    /// all, which separates "you have no key" from "this program could never see the
    /// one you have".
    /// </remarks>
    private static int Keys(PluginCatalog plugins, Credentials credentials, TextWriter output)
    {
        output.WriteLine(credentials.Store is { } store
            ? $"Keys kept by {store.Name}."
            : "Nothing installed here can hold a key, so only the environment is read.");

        output.WriteLine();

        foreach (var assistant in plugins.Assistants.OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            var variable = assistant.Credential.EnvironmentVariable;

            var where = credentials.SourceOf(assistant.Id, variable) switch
            {
                CredentialSource.Kept => $"kept by {credentials.Store?.Name}",
                CredentialSource.Environment => $"from {variable}",

                // Never reachable from here — a session key belongs to a window
                // — but named rather than folded into "no key", because a reader
                // comparing this against the panel deserves the same words.
                CredentialSource.Session => "held for this run only",
                _ => $"none — set {variable}",
            };

            var asked = assistant is IModelSurvey ? string.Empty : ", and cannot be surveyed";

            output.WriteLine($"  {assistant.Id} — {where}{asked}");
        }

        return Exit.Ok;
    }

    private static void Say(IReadOnlyList<ModelReport> found, TextWriter output, bool json)
    {
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(found, Writing.Json));

            return;
        }

        output.WriteLine();

        foreach (var model in found)
        {
            var senses = new List<string>();

            if (model.Vision) senses.Add("sees");
            if (model.Hearing) senses.Add("hears");
            if (model.Least is { } least) senses.Add($"thinks {least}..{model.Most}");

            output.WriteLine($"  {model.Id} — {(senses.Count == 0 ? "text only" : string.Join(", ", senses))}");
        }
    }
}
