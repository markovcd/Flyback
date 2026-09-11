using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.Gemini;

/// <summary>
/// Offers an assistant that speaks Google's generateContent format. A second
/// adapter rather than a second base url, which is the whole reason it is worth
/// having: what it buys is in <see cref="Wire.Answers"/> — a sound is an ordinary
/// part of a turn here, so the model building the patch can be played the patch.
/// </summary>
public sealed class GeminiPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.gemini",
        "Gemini",
        "Builds patches through Google's Gemini models, which hear the patch as well as see it.");

    public void Register(IPluginRegistry registry) => registry.AddPatchAssistant(new GeminiAssistant());
}

public sealed partial class GeminiAssistant : IPatchAssistant
{
    public string Id => "gemini";

    public string Name => "Gemini";

    /// <summary>
    /// Below the chat-completions adapter on purpose.
    /// </summary>
    /// <remarks>
    /// Priority decides what a fresh install starts on, and starting somewhere
    /// that needs a key nobody has is worse than starting on the one that has
    /// been there. Anybody who wants this picks it from the list once and the
    /// settings remember it.
    /// </remarks>
    public int Priority => 40;

    /// <summary>
    /// Where somebody starts before anybody has asked the endpoint anything.
    /// </summary>
    /// <remarks>
    /// Flash rather than Pro: it sees, it hears, it thinks, and it is the one
    /// somebody can point at a key from a free tier, which is what makes "bring your
    /// own key" a real offer. Pro is one line away in the box.
    /// <para>
    /// A written-down list is the part that goes stale, and the answer is
    /// <see cref="IModelSurvey"/> — see <see cref="AssistantSchema.Surveyed"/>.
    /// Until one has been run these four are the whole of what is known here, and
    /// any of them may already answer 404, which is why the list is short rather
    /// than exhaustive.
    /// </para>
    /// </remarks>
    public AssistantSchema Schema { get; } = new(
        "gemini-3.6-flash",
        [
            new AssistantModel("gemini-3.1-pro-preview", Hearing: true),
            new AssistantModel("gemini-3.8-flash", Hearing: true),
            new AssistantModel("gemini-3.6-flash", Hearing: true),
            new AssistantModel("gemini-3.5-flash-lite", Hearing: true),
        ],
        "GEMINI_API_KEY",
        "A key from Google AI Studio. The endpoint is fixed — this format is "
        + "spoken in one place, unlike chat completions.",
        "https://generativelanguage.googleapis.com/v1beta");

    /// <summary>What this one's key comes from. The host holds it; see ADR-0034.</summary>
    public AssistantCredential Credential => Schema.Credential;

    /// <summary>
    /// The ordinary five questions, declared by the schema rather than written out
    /// here — see <see cref="AssistantSchema.Form"/>. The endpoint arrives fixed
    /// rather than absent, because this format is spoken in one place.
    /// </summary>
    /// <remarks>
    /// The model box offers what a survey found, where one has been run, and every
    /// question that reads a model goes through the same substitution: a box
    /// offering surveyed models while <see cref="Senses"/> answered from the
    /// written-down ones would be a form that lies.
    /// </remarks>
    public IReadOnlyList<AssistantField> Form(AssistantValues values) => Schema.Surveyed(values).Form(values);

    public AssistantSenses Senses(AssistantValues values) => Schema.Surveyed(values).Senses(values);

    /// <summary>
    /// Answered from the configuration alone — no request, no client, nothing
    /// that costs anything. The endpoint is only found out to be wrong when
    /// somebody actually asks it something, which is the honest moment for it.
    /// </summary>
    public string? Unavailable(AssistantConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            return "No key yet — set GEMINI_API_KEY, or put one in Settings.";

        var chosen = Schema.Surveyed(config.Values).Read(config.Values);

        if (string.IsNullOrWhiteSpace(chosen.Model))
            return "No model chosen. Put one in Settings.";

        var endpoint = chosen.BaseUrl ?? Schema.DefaultBaseUrl;

        if (string.IsNullOrWhiteSpace(endpoint))
            return "No endpoint. Put one in Settings.";

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https"
                ? null
                : $"'{endpoint}' is not an http or https address.";
    }

    public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => Session(workbench, config);

    public IPatchSession? Resume(PatchWorkbench workbench, AssistantConfig config, string saved)
    {
        var session = Session(workbench, config);

        if (session.Take(saved)) return session;

        session.Dispose();
        return null;
    }

    private GeminiSession Session(PatchWorkbench workbench, AssistantConfig config)
    {
        var schema = Schema.Surveyed(config.Values);
        var chosen = schema.Read(config.Values);

        return new GeminiSession(
            workbench,
            chosen,
            config.ApiKey,
            Schema.DefaultBaseUrl!,
            Thinking(config.Values, chosen),
            ownEars: schema.Known(chosen.Model)?.Hearing == true);
    }

    /// <summary>
    /// The three words of effort as this provider spells them, or null where nothing
    /// can safely be said.
    /// </summary>
    /// <remarks>
    /// Medium is dynamic — the model decides for itself, which is what -1 means. Low
    /// and High are the ends of what the chosen model accepts, and those are
    /// per-model numbers that a budget out of range answers with a 400 rather than a
    /// clamp. So they are measured rather than written down, and a model nobody has
    /// measured gets no <c>thinkingConfig</c> at all.
    /// </remarks>
    private static JsonObject? Thinking(AssistantValues values, AssistantChoices chosen)
    {
        // Qualified because this class also has a Survey, and the method would
        // otherwise win the name over the type that stores what it found.
        var measured = Assist.Survey.Read(values.Text(Assist.Survey.Key, string.Empty))
            .FirstOrDefault(m => string.Equals(m.Id, chosen.Model, StringComparison.OrdinalIgnoreCase));

        if (measured?.Least is not { } least || measured.Most is not { } most) return null;

        var tokens = chosen.Effort switch
        {
            AssistantEffort.Low => least,
            AssistantEffort.High => most,
            _ => -1,
        };

        return new JsonObject { ["thinkingBudget"] = tokens };
    }
}
