using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// Asks a decision model typed questions about a piece of text, says which models there are,
/// or downloads one: every path the editor's decisions take, from a terminal.
/// </summary>
internal static class DecideCommand
{
    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var state = new Argument<string?>("state")
        {
            Description = "The text the questions are about, or - to read it from standard input.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var ask = new Option<FileInfo?>("--ask") { Description = "A file of questions in the System One format." };
        var model = new Option<string?>("--model") { Description = "Which decision model, by id. Defaults to the one the settings choose." };
        var yesNo = new Option<string?>("--yes-no") { Description = "Ask one yes-no question." };
        var choice = new Option<string?>("--choice") { Description = "Ask one choice question; give its options with --option." };
        var option = new Option<string[]>("--option") { Description = "One option of --choice, as label=description." };
        var score = new Option<string?>("--score") { Description = "Ask one score question; give its levels, lowest first, with --level." };
        var level = new Option<string[]>("--level") { Description = "One level of --score." };
        var status = new Option<bool>("--status") { Description = "List the decision models and whether each can answer." };
        var prepare = new Option<bool>("--prepare") { Description = "Download what the model needs to answer." };
        var yes = new Option<bool>("--yes", "-y") { Description = "Download without the question." };
        var use = new Option<string?>("--for")
        {
            Description = $"Ask, or --set, as one use does: {string.Join(", ", DecisionUse.All.Select(u => $"{u.Key} ({u.Value})"))}.",
        };
        var set = new Option<string[]>("--set")
        {
            Description = "A model setting, as key=value, for this run; with --for, for that use only. key= clears it. Repeatable.",
            AllowMultipleArgumentsPerToken = false,
        };
        var save = new Option<bool>("--save") { Description = "Keep --model as the chosen model, and --set in the settings, for the editor and later runs." };
        var settings = DecisionSettingsOption.Create();

        var command = new Command("decide", "Ask a decision model typed questions about some text, and get probabilities back.")
        {
            state, ask, model, yesNo, choice, option, score, level, status, prepare, yes, use, set, save, settings, json,
        };

        command.SetAction((result, cancellation) => Run(
            plugins.Catalog,
            new DecideOptions(
                result.GetValue(state),
                result.GetValue(ask),
                result.GetValue(model),
                result.GetValue(yesNo),
                result.GetValue(choice),
                result.GetValue(option) ?? [],
                result.GetValue(score),
                result.GetValue(level) ?? [],
                result.GetValue(status),
                result.GetValue(prepare),
                result.GetValue(yes),
                result.GetValue(json))
            {
                Use = result.GetValue(use),
                Set = result.GetValue(set) ?? [],
                Save = result.GetValue(save),
            },
            Console.In,
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error,
            cancellation,
            settingsPath: result.GetValue(settings),
            asking: Console.IsInputRedirected ? null : Console.In));

        return command;
    }

    /// <param name="input">Where a state of <c>-</c> is read from.</param>
    /// <param name="settingsPath">Somewhere other than the usual place, for the tests.</param>
    /// <param name="modelsRoot">Where models' files are kept, for the tests.</param>
    /// <param name="asking">Where the download question is answered, or null where nobody can answer it.</param>
    /// <param name="http">What downloads go over, for the tests.</param>
    public static async Task<int> Run(
        PluginCatalog plugins,
        DecideOptions options,
        TextReader input,
        TextWriter output,
        TextWriter error,
        CancellationToken cancel,
        string? settingsPath = null,
        string? modelsRoot = null,
        TextReader? asking = null,
        HttpClient? http = null)
    {
        var settings = DecisionSettings.Load(settingsPath);
        var decisions = new Decisions(
            plugins,
            settings,
            new Credentials(plugins.PreferredSecretStore),
            new ModelStore(modelsRoot ?? ModelStore.DefaultRoot));

        if (options.Use is { } unknown && !DecisionUse.All.ContainsKey(unknown))
        {
            error.WriteLine($"There is no use called '{unknown}'. The uses: {string.Join(", ", DecisionUse.All.Keys)}.");
            return Exit.Failed;
        }

        if (options.Status) return Status(decisions, options.Json, output);

        var model = options.Model is { } id ? decisions.Model(id) : decisions.Chosen;

        if (model is null)
        {
            error.WriteLine(options.Model is { } named ? $"No decision model called '{named}' is installed." : decisions.Unavailable());
            if (decisions.Models.Count > 0) error.WriteLine($"Installed: {string.Join(", ", decisions.Models.Select(m => m.Id))}.");
            return Exit.Failed;
        }

        if (!Set(settings, model, options, error)) return Exit.Failed;

        if (options.Save)
        {
            if (options.Model is null && options.Set.Count == 0)
            {
                error.WriteLine("--save keeps what --model and --set say; give one of them.");
                return Exit.Failed;
            }

            if (options.Model is not null) settings.Model = model.Id;

            try
            {
                settings.Save(settingsPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"The settings could not be saved: {ex.Message}");
                return Exit.Failed;
            }

            output.WriteLine(Saved(settings, model, options.Use));

            if (!Asks(options)) return Exit.Ok;
        }

        if (options.Prepare) return await Prepare(decisions, model, options, output, error, asking, http, cancel).ConfigureAwait(false);

        if (Questions(options, error) is not { } questions) return Exit.Failed;

        var text = options.State == "-" ? await input.ReadToEndAsync(cancel).ConfigureAwait(false) : options.State;

        if (text is null)
        {
            error.WriteLine("Give the text to ask about, or - to read it from standard input.");
            return Exit.Failed;
        }

        Decision decision;

        try
        {
            decision = await decisions.Decide(model, new DecisionRequest(text, questions), cancel, options.Use).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or ArgumentException)
        {
            error.WriteLine(ex.Message);
            return Exit.Failed;
        }

        if (options.Json) output.WriteLine(SystemOneWire.Answer(decision));
        else Print(model, decision, output);

        return Exit.Ok;
    }

    /// <summary>
    /// Lays <see cref="DecideOptions.Set"/> into <paramref name="settings"/>: over the model's own
    /// settings, or with a use, over what that use lays on them. False having said which pair is not one.
    /// </summary>
    private static bool Set(DecisionSettings settings, IDecisionModel model, DecideOptions options, TextWriter error)
    {
        if (options.Set.Count == 0) return true;

        var held = new Dictionary<string, string>((options.Use is { } use ? settings.Over(model.Id, use) : settings.Of(model.Id)).All, StringComparer.Ordinal);
        var keys = model.Form(settings.Of(model.Id, options.Use)).Select(f => f.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var pair in options.Set)
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var key = equals > 0 ? pair[..equals].Trim() : "";

            if (key.Length == 0)
            {
                error.WriteLine($"--set {pair}: write it as key=value, such as model=typed-decisions.");
                return false;
            }

            if (!keys.Contains(key))
            {
                error.WriteLine($"--set {pair}: {model.Name} has no setting '{key}'. It has: {(keys.Count == 0 ? "none" : string.Join(", ", keys.Order(StringComparer.Ordinal)))}.");
                return false;
            }

            var value = pair[(equals + 1)..].Trim();

            if (value.Length == 0) held.Remove(key);
            else held[key] = value;
        }

        if (options.Use is { } asking) settings.Remember(model.Id, asking, new SettingValues(held));
        else settings.Remember(model.Id, new SettingValues(held));

        return true;
    }

    /// <summary>What was kept, as a sentence.</summary>
    private static string Saved(DecisionSettings settings, IDecisionModel model, string? use)
    {
        var values = use is null ? settings.Of(model.Id) : settings.Over(model.Id, use);
        var pairs = values.All.Count == 0 ? "nothing set" : string.Join(", ", values.All.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{v.Key}={v.Value}"));
        var chosen = settings.Model == model.Id ? $"{model.Name} is the chosen model" : $"{model.Name} is not the chosen model";

        return use is null ? $"Saved. {chosen}, with {pairs}." : $"Saved. {chosen}; for {use} it has {pairs} over its own settings.";
    }

    private static bool Asks(DecideOptions options) =>
        options.Ask is not null || options.YesNo is not null || options.Choice is not null || options.Score is not null || options.Prepare;

    /// <summary>The questions the options ask: a file, or one written out on the line.</summary>
    private static Dictionary<string, Question>? Questions(DecideOptions options, TextWriter error)
    {
        var asked = new Dictionary<string, Question>(StringComparer.Ordinal);

        if (options.Ask is { } file)
        {
            string json;

            try
            {
                json = File.ReadAllText(file.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"{file.Name} could not be read: {ex.Message}");
                return null;
            }

            if (SystemOneWire.ReadQuestions(json, out var problem) is not { } read)
            {
                error.WriteLine($"{file.Name}: {problem}");
                return null;
            }

            foreach (var (id, question) in read) asked[id] = question;
        }

        if (options.YesNo is { } statement) asked["yes_no"] = new Question.YesNo(statement);

        if (options.Choice is { } which)
        {
            var parsed = options.Options.Select(o => o.Split('=', 2)).Select(p => new ChoiceOption(p[0].Trim(), p.Length > 1 ? p[1].Trim() : "")).ToList();
            asked["choice"] = new Question.Choice(which, parsed);
        }

        if (options.Score is { } how) asked["score"] = new Question.Score(how, options.Levels);

        if (asked.Count == 0)
        {
            error.WriteLine("Ask something: --ask <file>, --yes-no, --choice with --option, or --score with --level.");
            return null;
        }

        return asked;
    }

    private static void Print(IDecisionModel model, Decision decision, TextWriter output)
    {
        output.WriteLine($"Answered by {model.Name}{(decision.Model.Length > 0 ? $" ({decision.Model})" : "")}.");

        foreach (var (id, answer) in decision.Answers)
        {
            output.WriteLine(answer switch
            {
                Answer.YesNo yes => $"  {id}: {(yes.Probability >= 0.5 ? "yes" : "no")}, {Number(yes.Probability)} that it holds",
                Answer.Chosen chosen => $"  {id}: {chosen.Option} ({Number(chosen.Probabilities.GetValueOrDefault(chosen.Option))}), confidence {Number(chosen.Confidence)}",
                Answer.Scored scored => $"  {id}: {Nearest(scored)} (score {Number(scored.Score)} of 0–{scored.Levels.Count - 1}), confidence {Number(scored.Confidence)}",
                _ => $"  {id}: an answer of no kind this knows",
            });
        }
    }

    private static string Nearest(Answer.Scored scored) =>
        scored.Levels.Count == 0 ? "?" : scored.Levels[(int)Math.Clamp(Math.Round(scored.Score), 0, scored.Levels.Count - 1)];

    private static string Number(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static int Status(Decisions decisions, bool json, TextWriter output)
    {
        var chosen = decisions.Chosen;

        var rows = decisions.Models
            .OrderByDescending(m => m.Priority)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .Select(m => new
            {
                id = m.Id,
                name = m.Name,
                chosen = ReferenceEquals(m, chosen),
                prepared = decisions.Store.Prepared(m),
                missingBytes = decisions.Store.Missing(m),
                unavailable = decisions.Unavailable(m),
                uses = DecisionUse.All.Keys
                    .Select(u => (use: u, over: decisions.Settings.Over(m.Id, u).All))
                    .Where(u => u.over.Count > 0)
                    .ToDictionary(u => u.use, u => u.over, StringComparer.Ordinal),
            })
            .ToList();

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(rows));
            return Exit.Ok;
        }

        if (rows.Count == 0) output.WriteLine("No decision model is installed.");

        foreach (var row in rows)
        {
            output.WriteLine($"{row.id,-12} {row.name,-20} {(row.chosen ? "chosen" : ""),-7} {row.unavailable ?? "ready"}");

            foreach (var (use, over) in row.uses)
                output.WriteLine($"{"",-12} for {use}: {string.Join(", ", over.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{v.Key}={v.Value}"))}");
        }

        if (chosen is null && rows.Count > 0) output.WriteLine(decisions.Unavailable());

        return Exit.Ok;
    }

    private static async Task<int> Prepare(
        Decisions decisions,
        IDecisionModel model,
        DecideOptions options,
        TextWriter output,
        TextWriter error,
        TextReader? asking,
        HttpClient? http,
        CancellationToken cancel)
    {
        if (model is not IPreparedModel prepared || decisions.Store.Prepared(model))
        {
            output.WriteLine($"{model.Name} has everything it needs.");
            return Exit.Ok;
        }

        var hosts = string.Join(", ", prepared.Needs.Select(f => f.Address.Host).Distinct(StringComparer.OrdinalIgnoreCase));
        var size = Decisions.Megabytes(decisions.Store.Missing(model));

        if (!Agreed($"{model.Name} needs {size} downloaded from {hosts}, into {decisions.Store.FolderOf(model)}.", options, asking, output, error))
            return Exit.Failed;

        using var own = http is null ? new HttpClient { Timeout = Timeout.InfiniteTimeSpan } : null;
        var shown = ConsoleProgress.For("downloading");
        var progress = shown is null ? null : new Progress<(long Done, long Total)>(p => shown.Report(p.Total == 0 ? 1 : (double)p.Done / p.Total));

        try
        {
            await decisions.Store.Prepare(model, http ?? own!, progress, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or HttpRequestException or IOException)
        {
            error.WriteLine(ex.Message);
            return Exit.Failed;
        }

        output.WriteLine($"{model.Name} is ready.");
        return Exit.Ok;
    }

    /// <summary>Says what is about to be downloaded and waits for a typed yes; nobody to ask is not a yes.</summary>
    private static bool Agreed(string what, DecideOptions options, TextReader? asking, TextWriter output, TextWriter error)
    {
        if (options.Yes) return true;

        if (asking is null)
        {
            error.WriteLine(what);
            error.WriteLine("This asks before it downloads, and there is nobody here to ask. Pass --yes to go ahead.");
            return false;
        }

        var talk = options.Json ? error : output;

        talk.WriteLine(what);
        talk.Write("Download? [y/N] ");

        var answer = asking.ReadLine()?.Trim();
        var agreed = string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);

        talk.WriteLine();
        if (!agreed) talk.WriteLine("Nothing was downloaded.");

        return agreed;
    }
}
