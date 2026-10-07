using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Hosting;
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

        var command = new Command("decide", "Ask a decision model typed questions about some text, and get probabilities back.")
        {
            state, ask, model, yesNo, choice, option, score, level, status, prepare, yes, json,
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
                result.GetValue(json)),
            Console.In,
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error,
            cancellation,
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
        var decisions = new Decisions(
            plugins,
            DecisionSettings.Load(settingsPath),
            new Credentials(plugins.PreferredSecretStore),
            new ModelStore(modelsRoot ?? ModelStore.DefaultRoot));

        if (options.Status) return Status(decisions, options.Json, output);

        var model = options.Model is { } id ? decisions.Model(id) : decisions.Chosen;

        if (model is null)
        {
            error.WriteLine(options.Model is { } named ? $"No decision model called '{named}' is installed." : decisions.Unavailable());
            if (decisions.Models.Count > 0) error.WriteLine($"Installed: {string.Join(", ", decisions.Models.Select(m => m.Id))}.");
            return Exit.Failed;
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
            decision = await decisions.Decide(model, new DecisionRequest(text, questions), cancel).ConfigureAwait(false);
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
            })
            .ToList();

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(rows));
            return Exit.Ok;
        }

        if (rows.Count == 0) output.WriteLine("No decision model is installed.");

        foreach (var row in rows)
            output.WriteLine($"{row.id,-12} {row.name,-20} {(row.chosen ? "chosen" : ""),-7} {row.unavailable ?? "ready"}");

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
