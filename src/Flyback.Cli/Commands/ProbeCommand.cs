using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

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
    /// <summary>
    /// A command about an assistant rather than about a patch, which is why it
    /// needs the plugin catalog rather than the engine: what it asks and what
    /// it writes both belong to a plugin.
    /// </summary>
    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var provider = new Option<string>("--provider")
        {
            Description = "Which assistant to ask, by id, or `all` for every one with a key. "
                + "Defaults to whichever the settings are on.",
        };

        var model = new Option<string[]>("--model")
        {
            Description = "Ask about these models by name, whether or not the endpoint lists them.",
            AllowMultipleArgumentsPerToken = true,
        };

        var all = new Option<bool>("--all")
        {
            Description = "Ask about everything listed, not only what could build a patch.",
        };

        var bounds = new Option<bool>("--bounds")
        {
            Description = "Also measure what each model will think for. Slow, and billed as thinking.",
        };

        var dry = new Option<bool>("--dry-run")
        {
            Description = "Print what was found and leave the settings as they are.",
        };

        var keys = new Option<bool>("--keys")
        {
            Description = "Say where each provider's key would come from, and ask nothing of anybody.",
        };

        var yes = new Option<bool>("--yes", "-y")
        {
            Description = "Start without the question. A probe is billed traffic, so it is asked for "
                + "first unless this says not to.",
        };

        var command = new Command(
            "probe",
            "Ask an assistant's endpoint which models it has and what each one accepts.")
        {
            provider, model, all, bounds, dry, keys, yes, json,
        };

        command.SetAction((result, cancellation) => ProbeCommand.Run(
            plugins.Catalog,
            new ProbeOptions(
                result.GetValue(provider),
                result.GetValue(model) ?? [],
                result.GetValue(all),
                result.GetValue(bounds),
                result.GetValue(dry),
                result.GetValue(json),
                result.GetValue(keys),
                result.GetValue(yes)),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error,
            cancellation,
            asking: Console.IsInputRedirected ? null : Console.In));

        return command;
    }

    /// <param name="settingsPath">
    /// Somewhere other than the usual place, for the tests. A command that both
    /// reads and writes the real file is one no test can call safely.
    /// </param>
    /// <param name="asking">
    /// Where the question is answered, or null where there is nobody to answer it —
    /// a script, a pipe, a test. Whether this machine has a console is the caller's
    /// to know; what to do about it is here.
    /// </param>
    public static async Task<int> Run(
        PluginCatalog plugins,
        ProbeOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancel,
        string? settingsPath = null,
        TextReader? asking = null)
    {
        var settings = AssistantSettings.Load(settingsPath);

        // Built once and used for both paths, so that what --keys reports is
        // literally what the probe would go on to use rather than a second
        // opinion about it.
        var credentials = new Credentials(plugins.PreferredSecretStore);

        if (options.Keys) return Keys(plugins, credentials, output);

        if (Everyone(plugins, options.Provider))
        {
            if (plugins.Assistants.Count == 0)
            {
                error.WriteLine("No assistant is installed.");

                return Exit.Failed;
            }

            if (plugins.Assistants.Any(one => Ready(one, credentials))
                && !Agreed(options, asking, output, error))
            {
                return Exit.Failed;
            }

            return await Each(plugins, settings, credentials, options, output, error, settingsPath, cancel).ConfigureAwait(false);
        }

        // The named one, then the one this machine was left on, then whichever
        // the catalog would put in front of somebody. The middle is what makes
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

        if (Ready(assistant, credentials) && !Agreed(options, asking, output, error)) return Exit.Failed;

        return await One(settings, credentials, assistant, options, output, error, settingsPath, cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether this one would actually send anything: it can be surveyed, and
    /// there is a key to survey it with.
    /// </summary>
    /// <remarks>
    /// What the question is gated on. Nothing is spent without both, and
    /// <see cref="One"/> says which is missing far better than a question could —
    /// being asked to agree to a charge and then told there was never going to be
    /// one is worse than either sentence alone.
    /// </remarks>
    private static bool Ready(IPatchAssistant assistant, Credentials credentials) =>
        assistant is IModelSurvey
        && credentials.SourceOf(assistant.Id, assistant.Credential.EnvironmentVariable) != CredentialSource.None;

    /// <summary>
    /// Says what this is about to spend and waits for a yes.
    /// </summary>
    /// <remarks>
    /// Asked once however many providers are about to be walked, and asked before
    /// anything is sent — which is the whole of what it is for. <c>--dry-run</c> is
    /// no excuse to skip it: a dry run asks the endpoint everything a real one does
    /// and is billed for all of it, and only declines to write the answer down.
    /// <para>
    /// Nobody to ask is not a yes. That is what <c>--yes</c> is for, and saying so is
    /// more use than a command that hangs on a pipe, or one that helps itself to
    /// somebody's money because nothing objected.
    /// </para>
    /// </remarks>
    private static bool Agreed(ProbeOptions options, TextReader? asking, TextWriter output, TextWriter error)
    {
        if (options.Yes) return true;

        if (asking is null)
        {
            error.WriteLine("This asks before it spends anything, and there is nobody here to ask.");
            error.WriteLine("Pass --yes to go ahead without the question.");

            return false;
        }

        // Beside the answer rather than in it: under --json this command's output
        // is a document somebody is piping somewhere, and a question written into
        // it is a question nobody sees and a file nothing can parse.
        var talk = options.Json ? error : output;

        talk.WriteLine("A probe is real traffic on a real key, and every question it asks is billed.");
        talk.WriteLine(
            "Each model is asked to answer, then to take a picture, then to take a sound — three "
            + "requests apiece, and a provider's list runs to dozens of models.");

        if (options.Bounds)
        {
            talk.WriteLine(
                "--bounds adds a search per model on top of that, and makes each one think for real "
                + "near the top of its range.");
        }

        talk.Write("Go ahead? [y/N] ");

        var answer = asking.ReadLine()?.Trim();

        // Yes is typed out or it is not yes. Nothing else — no default, no empty
        // line, no end of input — is taken for agreement to spend money.
        var agreed = string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);

        talk.WriteLine();

        if (!agreed) talk.WriteLine("Nothing was asked.");

        return agreed;
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
        string? settingsPath,
        CancellationToken cancel)
    {
        var any = false;
        var first = true;

        foreach (var assistant in plugins.Assistants.OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            if (!first) output.WriteLine();

            first = false;

            var code = await One(settings, credentials, assistant, options, output, error, settingsPath, cancel)
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
        string? settingsPath,
        CancellationToken cancel)
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
        var values = settings.Of(assistant.Id);
        var config = new AssistantConfig(credentials.Transport(assistant, values), values);

        if (assistant.NeedsKey && !config.Transport.HasKey)
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

        if (Credentials.Elsewhere(assistant, config) is { } elsewhere)
        {
            error.WriteLine(elsewhere);

            return Exit.Failed;
        }

        if (!options.Json)
        {
            output.WriteLine($"{assistant.Name}, with a key from {credentials.SourceOf(assistant.Id, variable)}.");
            output.WriteLine();
        }

        IReadOnlyList<ModelReport> found;

        try
        {
            found = await survey.Survey(
                config,
                new SurveyOptions(options.Only, options.All, options.Bounds),
                options.Json ? null : new Commentary(output),
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
            output.WriteLine($"{Writing.Count(found.Count, "model")} written to {SettingsFile.Path}.");
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

    /// <summary>A survey's running commentary, written as it is reported.</summary>
    /// <remarks>
    /// Deliberately not <see cref="Progress{T}"/>. With no synchronization context to
    /// capture — which is every way this command is run — that one hands each callback to
    /// the thread pool, so a line can be written after the command has returned, and two
    /// of them can be written at once. A command's output is a transcript: it is written
    /// on the thread that reported it, in the order it happened.
    /// </remarks>
    private sealed class Commentary(TextWriter output) : IProgress<string>
    {
        public void Report(string value) => output.WriteLine(value);
    }
}
