namespace Flyback.Plugins.Assist;

/// <summary>How hard an assistant should think before answering.</summary>
/// <remarks>
/// Three words rather than a number, because every provider spells this
/// differently and some do not offer it at all. An adapter maps what it can and
/// ignores the rest.
/// </remarks>
public enum AssistantEffort
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>
/// One model a provider suggests, and what it will accept being handed.
/// </summary>
/// <remarks>
/// The capabilities are here rather than in the shell because the shell must not
/// know one model name from another — that is the boundary ADR-0025 drew and
/// ADR-0033 kept. A provider knows what its own models take; the form in front
/// of somebody only knows that a model either takes a picture or does not.
/// <para>
/// A suggestion, not a whitelist. Anything may be typed, because
/// <see cref="AssistantSchema.BaseUrlEditable"/> means the endpoint may be one
/// nobody here has heard of.
/// </para>
/// </remarks>
/// <param name="Id">What goes in the request.</param>
/// <param name="Vision">Whether it accepts a picture. Nearly all of them do.</param>
/// <param name="Hearing">
/// Whether it accepts a sound. Most do not — see
/// <see cref="AssistantConfig.Hearing"/>.
/// <para>
/// True <em>and</em> <paramref name="Vision"/> true is the interesting case and
/// the one everything downstream turns on: a model that takes both can drive the
/// conversation and be played the clip itself, so the run has no second model,
/// no <see cref="AssistantConfig.EarModel"/>, and a briefing that says "you can
/// hear" rather than "you have an ear" — see <see cref="Listener"/>.
/// </para>
/// </param>
public sealed record AssistantModel(string Id, bool Vision = true, bool Hearing = false);

/// <summary>
/// What a provider needs configured, so the shell can put a form in front of
/// someone without knowing which provider it is talking about.
/// </summary>
/// <param name="EnvironmentVariable">
/// The variable this provider is conventionally given its key in. The shell
/// reads it, not the plugin: a credential is the host's to hold, and a plugin
/// that went looking for one would be a plugin that could keep it.
/// </param>
/// <param name="CredentialHelp">One line saying where a key comes from, shown under the field.</param>
/// <param name="DefaultBaseUrl">Null when the endpoint is not the caller's business.</param>
/// <param name="BaseUrlEditable">
/// True only where pointing somewhere else is the point — an OpenAI-shaped
/// endpoint reaches a dozen providers and a local runtime besides.
/// </param>
public sealed record AssistantSchema(
    string DefaultModel,
    IReadOnlyList<AssistantModel> SuggestedModels,
    string EnvironmentVariable,
    string CredentialHelp,
    string? DefaultBaseUrl = null,
    bool BaseUrlEditable = false)
{
    /// <summary>
    /// What is known about the model somebody has typed, or null when it is not
    /// one of these.
    /// </summary>
    /// <remarks>
    /// Null is not "cannot": it is "nobody here knows", which is the ordinary
    /// state of a model at an endpoint this was pointed at by hand — and it
    /// leaves every switch on the form where it was, rather than taking one
    /// away on a guess.
    /// </remarks>
    public AssistantModel? Known(string? model) =>
        string.IsNullOrWhiteSpace(model)
            ? null
            : SuggestedModels.Where(m => IsOne(model, m.Id)).MaxBy(m => m.Id.Length);

    /// <summary>
    /// Every model here that will take a sound, in the order the provider listed
    /// them — what the form offers as an ear, and empty for a provider that has
    /// none.
    /// </summary>
    public IEnumerable<AssistantModel> Ears => SuggestedModels.Where(m => m.Hearing);

    /// <summary>
    /// Whether a typed name is one of ours: the name itself, or that name with a
    /// date or a version after it.
    /// </summary>
    /// <remarks>
    /// The suffix has to begin with a digit, and that is the whole of the rule.
    /// A bare prefix match is not good enough and the reason is a real one:
    /// <c>gpt-4o-transcribe</c> begins with <c>gpt-4o</c> and is not a
    /// <c>gpt-4o</c> — it is a different model with different capabilities, and
    /// reading it as one would answer a question nobody here can answer, in the
    /// direction that takes a switch away. <c>gpt-4o-2024-11-20</c> is the other
    /// case, and a date is what tells them apart: a word after the name is
    /// another model, a number after it is the same one pinned to a day.
    /// <para>
    /// Where two could match, the longer wins — so a list holding both
    /// <c>gpt-4o</c> and <c>gpt-4o-mini</c> reads a dated mini as a mini.
    /// </para>
    /// </remarks>
    private static bool IsOne(string typed, string id)
    {
        if (typed.Equals(id, StringComparison.OrdinalIgnoreCase)) return true;
        if (!typed.StartsWith(id, StringComparison.OrdinalIgnoreCase)) return false;

        var rest = typed.AsSpan(id.Length);

        return rest.Length > 1 && rest[0] == '-' && char.IsAsciiDigit(rest[1]);
    }
}

/// <summary>
/// One configured provider, ready to be asked something.
/// </summary>
/// <remarks>
/// <see cref="ApiKey"/> is the only secret in the contract and it lives no
/// longer than the run. It is never written to the settings file, never logged,
/// and never repeated back in a message — see ADR-0034.
/// </remarks>
/// <param name="Vision">Whether the model may be shown a rendered frame.</param>
/// <param name="Hearing">
/// Whether the patch's sound may be listened to at all. Off by default, and the
/// asymmetry with <paramref name="Vision"/> is the point: every model this
/// reaches can be shown a picture, and only some can be played a sound. Who
/// does the listening is <paramref name="EarModel"/>'s question.
/// </param>
/// <param name="EarModel">
/// The model asked to listen <em>instead of</em> the one doing the building, or
/// null where no second model is wanted.
/// <para>
/// Null carries two quite different situations and the adapter can tell them
/// apart from its own schema. Where the driving model takes a sound, null means
/// there is nobody else to ask: the clip goes into the conversation, the way a
/// rendered frame does, and one model both builds and hears. Where it does not,
/// null means nothing has been chosen and <paramref name="Hearing"/> has nothing
/// to act on.
/// </para>
/// <para>
/// A second model is the older arrangement and still the common one — ADR-0047
/// records why it is forced on the chat-completions format. The models there
/// that take a sound require every request to carry one, so a conversation
/// driven by one is refused on its first turn, before anything exists to listen
/// to, and they do not take a picture besides.
/// </para>
/// </param>
public sealed record AssistantConfig(
    string ApiKey,
    string Model,
    string? BaseUrl = null,
    bool Vision = true,
    bool Hearing = false,
    string? EarModel = null,
    AssistantEffort Effort = AssistantEffort.Medium);

/// <summary>
/// Something that can be asked for a patch, before any conversation exists.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="IPatchSession"/> for the reason
/// <see cref="Audio.IAudioOutput"/> is kept apart from
/// <see cref="Audio.IAudioDevice"/>: the shell lists what is installed, and says
/// so in the status bar, without opening a connection or spending anything.
/// </remarks>
public interface IPatchAssistant
{
    /// <summary>Stable identifier, e.g. <c>anthropic</c>. What a setting names.</summary>
    string Id { get; }

    /// <summary>What a person should see, e.g. <c>Claude</c>.</summary>
    string Name { get; }

    /// <summary>Higher wins when several are installed. Ties break on <see cref="Id"/>.</summary>
    int Priority { get; }

    AssistantSchema Schema { get; }

    /// <summary>
    /// Why this configuration cannot run, or null when it can.
    /// </summary>
    /// <remarks>
    /// A sentence rather than <see cref="Audio.IAudioOutput.IsSupported"/>'s
    /// bool, because the answer here is usually one the person can act on — a
    /// key that is not set is not the same kind of no as an operating system
    /// that is not this one. Must answer without a network call and without
    /// throwing; a throw is taken as a no.
    /// </remarks>
    string? Unavailable(AssistantConfig config);

    /// <summary>
    /// Begins a conversation over one workbench. The workbench belongs to the
    /// host; the assistant only drives it.
    /// </summary>
    IPatchSession Start(PatchWorkbench workbench, AssistantConfig config);
}

/// <summary>
/// A conversation in progress.
/// </summary>
/// <remarks>
/// Multi-turn on purpose. The second instruction — "more blue, and slower" — is
/// the common one, and it should keep both the history and whatever prompt cache
/// the provider built for the first.
/// </remarks>
public interface IPatchSession : IDisposable
{
    /// <summary>
    /// One turn. Yields as work happens and completes when the assistant stops.
    /// </summary>
    /// <remarks>
    /// Never throws for a provider failure — that is a
    /// <see cref="PatchEvent.Failed"/>, so a bad key or a dropped connection
    /// costs the turn rather than the window. Cancellation ends the sequence and
    /// leaves the workbench wherever it got to, which is safe because the
    /// workbench is a copy.
    /// </remarks>
    IAsyncEnumerable<PatchEvent> Ask(string instruction, CancellationToken cancel);
}
