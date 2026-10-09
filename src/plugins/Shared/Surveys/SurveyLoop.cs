using System.Net;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.Surveys;

/// <summary>
/// One survey of one endpoint, whichever provider's: the bare ping, then a picture,
/// then a sound, for each model asked about.
/// </summary>
internal static class SurveyLoop
{
    public static async Task<IReadOnlyList<ModelReport>> Run(
        IModelProbe probe,
        SurveyOptions options,
        IProgress<string>? said,
        CancellationToken cancel)
    {
        // A run naming its models asks the catalog nothing, so an endpoint
        // with no catalog is still one this works against.
        var chosen = options.Only is { Count: > 0 } named
            ? named
            : (await probe.Catalog(said, cancel).ConfigureAwait(false))
                .Where(m => options.All || probe.Candidate(m))
                .ToList();

        said?.Report($"Asking about {chosen.Count}.");

        if (options.Bounds && !probe.Thinks)
            said?.Report("Nothing here has a thinking budget to measure; asking the other questions only.");

        var found = new List<ModelReport>();

        foreach (var model in chosen)
        {
            cancel.ThrowIfCancellationRequested();

            var (report, unsure) = await Look(probe, model, cancel).ConfigureAwait(false);

            if (report is null)
            {
                said?.Report($"{model}: no");
                continue;
            }

            if (options.Bounds && probe.Thinks
                && await probe.Bounds(model, said, cancel).ConfigureAwait(false) is var (least, most))
                report = report with { Least = least, Most = most };

            found.Add(report);
            said?.Report($"{model}: {Says(report, unsure)}");
        }

        return found;
    }

    /// <summary>
    /// Sends one question and reads the answer as a verdict.
    /// </summary>
    /// <remarks>
    /// A refusal of the request is an answer about the model; anything else (a
    /// limit, an outage, a proxy) is an answer about the moment, and recording it
    /// as a capability would outlive the moment.
    /// </remarks>
    public static async Task<ProbeAnswer> Ask(
        IAssistantTransport transport,
        Uri endpoint,
        string body,
        CancellationToken cancel)
    {
        try
        {
            var response = await transport.Send(endpoint, body, cancel).ConfigureAwait(false);

            if (response.Succeeded) return new ProbeAnswer(ProbeVerdict.Took, string.Empty);

            return response.Status is (int)HttpStatusCode.BadRequest or (int)HttpStatusCode.NotFound
                ? new ProbeAnswer(ProbeVerdict.Refused, Probe.Detail(response.Body))
                : new ProbeAnswer(ProbeVerdict.Unclear, Probe.Detail(response.Body));
        }
        catch (HttpRequestException)
        {
            return new ProbeAnswer(ProbeVerdict.Unclear, string.Empty);
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new ProbeAnswer(ProbeVerdict.Unclear, string.Empty);
        }
    }

    /// <summary>
    /// What one model turned out to be, or null where it is not a model here, and
    /// whether any answer was unclear.
    /// </summary>
    /// <remarks>
    /// A refused ping ends it: a refused model is not a model with no senses, it is
    /// not a model here. The exception is a model that refuses every turn without a
    /// sound (ADR-0047), which is asked again with one.
    /// </remarks>
    private static async Task<(ModelReport? Report, bool Unsure)> Look(
        IModelProbe probe,
        string model,
        CancellationToken cancel)
    {
        var bare = await probe.Ask(model, null, null, cancel).ConfigureAwait(false);

        if (bare.Verdict is ProbeVerdict.Took)
        {
            var sees = await probe.Ask(model, Probe.Picture(), null, cancel).ConfigureAwait(false);
            var hears = await probe.Ask(model, null, Probe.Sound(), cancel).ConfigureAwait(false);

            // An unclear answer keeps the cautious value. A limit read as "takes a
            // sound" would send one to a model that refuses it and lose every turn
            // from the first listen onwards; read the other way it costs a setting
            // somebody can turn back on.
            var report = new ModelReport(model)
            {
                Vision = sees.Verdict is ProbeVerdict.Took,
                Hearing = hears.Verdict is ProbeVerdict.Took,
            };

            return (report, sees.Verdict is ProbeVerdict.Unclear || hears.Verdict is ProbeVerdict.Unclear);
        }

        if (bare.Verdict is not ProbeVerdict.Refused || !probe.OnlyHears(bare)) return (null, false);

        // Sight is not asked about: a model that refuses a turn without a sound
        // takes no picture either.
        var only = await probe.Ask(model, null, Probe.Sound(), cancel).ConfigureAwait(false);

        return only.Verdict is ProbeVerdict.Took
            ? (new ModelReport(model) { Vision = false, Hearing = true }, false)
            : (null, false);
    }

    private static string Says(ModelReport report, bool unsure)
    {
        var senses = new List<string>();

        if (report.Vision) senses.Add("sees");
        if (report.Hearing) senses.Add("hears");
        if (unsure) senses.Add("asked at a bad moment");
        if (report.Least is { } least) senses.Add($"thinks {least}..{report.Most}");

        return senses.Count == 0 ? "answers, and takes nothing else" : string.Join(", ", senses);
    }
}
