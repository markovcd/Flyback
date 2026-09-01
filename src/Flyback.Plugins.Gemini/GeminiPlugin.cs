using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.Gemini;

/// <summary>
/// Offers an assistant that speaks Google's generateContent format.
/// </summary>
/// <remarks>
/// A second adapter rather than a second base url, which is the whole reason it
/// is worth having: everything that speaks chat completions is already reachable
/// through the endpoint field on the other one, and this speaks something else.
/// What it buys is in <see cref="Wire.Answers"/> — a sound is an ordinary part
/// of a turn here, so the model building the patch can be played the patch.
/// </remarks>
public sealed class GeminiPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new(
        "flyback.gemini",
        "Gemini",
        "Builds patches through Google's Gemini models, which hear the patch as well as see it.");

    public void Register(IPluginRegistry registry) => registry.AddPatchAssistant(new GeminiAssistant());
}

/// <summary>
/// What one model will think for, in tokens.
/// </summary>
/// <remarks>
/// Kept here rather than on <see cref="AssistantModel"/> because it is this
/// provider's arithmetic and nothing in the shell can do anything with it. The
/// bounds are per-model and a budget outside them is a 400 rather than a clamp,
/// which is exactly why the other adapter sends no effort at all: it cannot know
/// what endpoint it is pointed at. This one can.
/// </remarks>
/// <param name="Least">The smallest budget this model accepts. Zero where it may be switched off.</param>
/// <param name="Most">The largest it accepts.</param>
internal sealed record Thought(int Least, int Most);

public sealed class GeminiAssistant : IPatchAssistant
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
    /// Every model here both sees and hears, which is the point of the adapter
    /// and not a coincidence of the list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Flash is the default rather than Pro. It sees, it hears, it thinks, and
    /// it is the one somebody can point at a key from a free tier — which is
    /// what makes "bring your own key" a real offer rather than a bill. Pro is a
    /// better builder for a hard patch and is one line away in the box.
    /// </para>
    /// <para>
    /// This list is the part that goes stale, exactly as ADR-0047 said it would:
    /// a model released next month is a stranger here until somebody adds a
    /// line. The failure stays in the safe direction — a stranger is offered
    /// everything and refused nothing — with one exception worth knowing about,
    /// which is that a stranger gets no thinking budget, because there is no
    /// telling what range it would accept.
    /// </para>
    /// </remarks>
    public AssistantSchema Schema { get; } = new(
        "gemini-2.5-flash",
        [
            new AssistantModel("gemini-2.5-pro", Hearing: true),
            new AssistantModel("gemini-2.5-flash", Hearing: true),
            new AssistantModel("gemini-2.5-flash-lite", Hearing: true),
        ],
        "GEMINI_API_KEY",
        "A key from Google AI Studio. The endpoint is fixed — this format is "
        + "spoken in one place, unlike chat completions.",
        "https://generativelanguage.googleapis.com/v1beta");

    /// <summary>
    /// What each model will think for.
    /// </summary>
    /// <remarks>
    /// Pro cannot be told not to think at all and its floor is 128; the two
    /// Flashes accept zero. Anything not named here is left alone entirely — see
    /// <see cref="Thinking"/>.
    /// </remarks>
    private static readonly Dictionary<string, Thought> Budgets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gemini-2.5-pro"] = new(128, 32768),
        ["gemini-2.5-flash"] = new(0, 24576),
        ["gemini-2.5-flash-lite"] = new(512, 24576),
    };

    /// <summary>
    /// Answered from the configuration alone — no request, no client, nothing
    /// that costs anything. The endpoint is only found out to be wrong when
    /// somebody actually asks it something, which is the honest moment for it.
    /// </summary>
    public string? Unavailable(AssistantConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            return "No key yet — set GEMINI_API_KEY, or put one in Settings.";

        if (string.IsNullOrWhiteSpace(config.Model))
            return "No model chosen. Put one in Settings.";

        var endpoint = config.BaseUrl ?? Schema.DefaultBaseUrl;

        if (string.IsNullOrWhiteSpace(endpoint))
            return "No endpoint. Put one in Settings.";

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https"
                ? null
                : $"'{endpoint}' is not an http or https address.";
    }

    public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
        new GeminiSession(
            workbench,
            config,
            Schema.DefaultBaseUrl!,
            Thinking(config),
            ownEars: Schema.Known(config.Model)?.Hearing == true);

    /// <summary>
    /// The three words of effort as this provider spells them, or null where
    /// nothing can safely be said.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Medium is dynamic — the model is told to decide for itself, which is what
    /// -1 means and what it would have done unasked. Low and High are the ends
    /// of what the chosen model accepts, which is why the numbers live in a
    /// table rather than in this method: they differ per model and a budget out
    /// of range is refused rather than clamped.
    /// </para>
    /// <para>
    /// A model nobody wrote down gets no <c>thinkingConfig</c> at all. This is
    /// the one place a stranger loses something, and it is the right way round:
    /// the alternative is guessing a number at a model whose floor might be
    /// above it and losing every request rather than one setting.
    /// </para>
    /// </remarks>
    private JsonObject? Thinking(AssistantConfig config)
    {
        if (Schema.Known(config.Model) is not { } known) return null;
        if (!Budgets.TryGetValue(known.Id, out var budget)) return null;

        var tokens = config.Effort switch
        {
            AssistantEffort.Low => budget.Least,
            AssistantEffort.High => budget.Most,
            _ => -1,
        };

        return new JsonObject { ["thinkingBudget"] = tokens };
    }
}
